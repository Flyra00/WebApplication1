using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Tests;

public class CustomerOrderTakeawayTests
{
    [Fact]
    public async Task Takeaway_Guest_WithoutTable_CanSubmitOrder()
    {
        await using var db = CreateDbContext();
        db.Products.Add(new Product
        {
            Name = "Nasi Goreng",
            Category = "Makanan",
            Price = 25000,
            Stock = 100,
            IsAvailable = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var product = await db.Products.FirstAsync();

        var result = await controller.Submit(new CustomerOrderController.SubmitOrderRequest
        {
            OrderType = OrderTypes.Takeaway,
            MembershipStatus = TableGuestTypes.Guest,
            PaymentMethod = PaymentMethods.Cash,
            GuestName = "Budi",
            GuestPhone = "081234567890",
            Items =
            [
                new CustomerOrderController.SubmitOrderItemRequest
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
        Assert.True(success);

        var savedOrder = await db.Orders.Include(o => o.TableSession).ThenInclude(s => s.Table).FirstOrDefaultAsync();
        Assert.NotNull(savedOrder);
        Assert.Equal(OrderTypes.Takeaway, savedOrder!.OrderType);
        Assert.Equal(TableSessionStatuses.Closed, savedOrder.TableSession.Status);
        Assert.Equal(-1, savedOrder.TableSession.Table.Number);
    }

    private static CustomerOrderController CreateController(AppDbContext db)
    {
        var midtransOptions = Options.Create(new MidtransOptions
        {
            ServerKey = "dummy-server",
            ClientKey = "dummy-client",
            IsProduction = false
        });
        var chargeOptions = Options.Create(new OrderChargesOptions
        {
            PpnPercentage = 0,
            ServicePercentage = 0
        });

        var controller = new CustomerOrderController(
            db,
            new MidtransService(new HttpClient(), midtransOptions),
            chargeOptions,
            NullLogger<CustomerOrderController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }
}
