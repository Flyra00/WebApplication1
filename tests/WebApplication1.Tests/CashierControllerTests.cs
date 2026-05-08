using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services.Midtrans;
using WebApplication1.Services.Time;
using System.Net;
using System.Text;

namespace WebApplication1.Tests;

public class CashierControllerTests
{
    [Fact]
    public async Task SubmitNewOrder_CreatesOrderAndDeductsStock()
    {
        await using var db = CreateDbContext();
        db.Tables.Add(new Table
        {
            Number = 12,
            Capacity = 4,
            IsActive = true
        });
        db.Products.Add(new Product
        {
            Name = "Ayam Bakar",
            Category = "Makanan",
            Price = 30000,
            Stock = 10,
            IsAvailable = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var product = await db.Products.FirstAsync();

        var result = await controller.SubmitNewOrder(new CashierController.SubmitNewOrderRequest
        {
            TableNumber = 12,
            Items =
            [
                new CashierController.SubmitNewOrderItemRequest
                {
                    ProductId = product.Id,
                    Qty = 3
                }
            ]
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var payload = json.Value!;
        var success = (bool)(payload.GetType().GetProperty("success")?.GetValue(payload) ?? false);

        Assert.True(success);

        db.ChangeTracker.Clear();
        var savedOrder = await db.Orders.Include(o => o.TableSession).ThenInclude(s => s!.Table).Include(o => o.Items).SingleAsync();
        Assert.NotNull(savedOrder.TableSession);
        Assert.NotNull(savedOrder.TableSession.Table);
        Assert.Equal(12, savedOrder.TableSession.Table.Number);
        Assert.Equal(OrderTypes.DineIn, savedOrder.OrderType);
        Assert.Equal(3, savedOrder.Items.Sum(item => item.Qty));

        var updatedProduct = await db.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(7, updatedProduct.Stock);
    }

    [Fact]
    public async Task SubmitNewOrder_RejectsWhenStockInsufficient()
    {
        await using var db = CreateDbContext();
        db.Tables.Add(new Table
        {
            Number = 15,
            Capacity = 4,
            IsActive = true
        });
        db.Products.Add(new Product
        {
            Name = "Es Teh",
            Category = "Minuman",
            Price = 8000,
            Stock = 1,
            IsAvailable = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var product = await db.Products.FirstAsync();

        var result = await controller.SubmitNewOrder(new CashierController.SubmitNewOrderRequest
        {
            TableNumber = 15,
            Items =
            [
                new CashierController.SubmitNewOrderItemRequest
                {
                    ProductId = product.Id,
                    Qty = 2
                }
            ]
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var payload = json.Value!;
        var success = (bool)(payload.GetType().GetProperty("success")?.GetValue(payload) ?? false);
        var error = payload.GetType().GetProperty("error")?.GetValue(payload)?.ToString();

        Assert.False(success);
        Assert.Contains("Stok", error ?? string.Empty);

        db.ChangeTracker.Clear();
        var unchangedProduct = await db.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(1, unchangedProduct.Stock);
        Assert.False(await db.Orders.AnyAsync());
    }

    [Fact]
    public async Task RequestPaymentToken_CreatesPendingMidtransPayment()
    {
        await using var db = CreateDbContext();
        var order = SeedOrder(db, total: 75000m);
        await db.SaveChangesAsync();

        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"token\":\"snap-token\",\"redirect_url\":\"https://example.com\"}", Encoding.UTF8, "application/json")
        });

        var controller = CreateController(db, handler);

        var result = await controller.RequestPaymentToken(new CashierController.CashierPaymentTokenRequest
        {
            OrderId = order.Id,
            PaymentMethod = PaymentMethods.Midtrans
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var payload = json.Value!;

        Assert.True((bool)(payload.GetType().GetProperty("success")?.GetValue(payload) ?? false));
        Assert.Equal(75000m, (decimal)(payload.GetType().GetProperty("amount")?.GetValue(payload) ?? 0m));

        db.ChangeTracker.Clear();
        var savedPayment = await db.Payments.SingleAsync();
        Assert.Equal(PaymentMethods.Midtrans, savedPayment.Method);
        Assert.Equal(PaymentStatuses.Pending, savedPayment.Status);
    }

    [Fact]
    public async Task SyncPaymentStatus_PaidResponseMarksOrderPaid()
    {
        await using var db = CreateDbContext();
        var order = SeedOrder(db, total: 75000m);
        var payment = new Payment
        {
            Order = order,
            Method = PaymentMethods.Midtrans,
            Purpose = PaymentPurpose.OrderRegular,
            Amount = 75000m,
            Status = PaymentStatuses.Pending,
            ReferenceNumber = "KSR-1-ORD-20260508-0001"
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"transaction_status\":\"settlement\",\"fraud_status\":\"accept\"}", Encoding.UTF8, "application/json")
        });

        var controller = CreateController(db, handler);

        var result = await controller.SyncPaymentStatus(new CashierController.CashierPaymentSyncRequest
        {
            PaymentId = payment.Id,
            StatusHint = "settlement"
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var payload = json.Value!;

        Assert.True((bool)(payload.GetType().GetProperty("success")?.GetValue(payload) ?? false));

        db.ChangeTracker.Clear();
        var savedPayment = await db.Payments.Include(x => x.Order).SingleAsync();
        Assert.Equal(PaymentStatuses.Paid, savedPayment.Status);
        Assert.Equal(OrderStatuses.Paid, savedPayment.Order.Status);
    }

    private static CashierController CreateController(AppDbContext db, HttpMessageHandler? handler = null, IBusinessTime? businessTime = null)
    {
        handler ??= new ScriptedHttpMessageHandler();
        var midtrans = new MidtransService(new HttpClient(handler), Options.Create(new MidtransOptions
        {
            ServerKey = "dummy-server",
            ClientKey = "dummy-client",
            IsProduction = false
        }));

        return new CashierController(db, midtrans, NullLogger<CashierController>.Instance, businessTime ?? TestBusinessTime.Create())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    private static Order SeedOrder(AppDbContext db, decimal total)
    {
        var table = new Table
        {
            Number = 99,
            Capacity = 4,
            IsActive = true
        };

        var session = new TableSession
        {
            Table = table,
            SessionCode = "SES-TEST-0001",
            GuestType = TableGuestTypes.Guest,
            Status = TableSessionStatuses.Open,
            StartTime = DateTime.UtcNow
        };

        var order = new Order
        {
            TableSession = session,
            OrderNumber = $"ORD-{Guid.NewGuid():N}".Substring(0, 20),
            OrderDate = DateTime.UtcNow,
            Status = OrderStatuses.Submitted,
            OrderType = OrderTypes.DineIn,
            Subtotal = total,
            Total = total
        };

        db.Tables.Add(table);
        db.TableSessions.Add(session);
        db.Orders.Add(order);
        return order;
    }

    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responses.Enqueue(responseFactory);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No scripted response available.");

            return Task.FromResult(_responses.Dequeue().Invoke(request));
        }
    }
}
