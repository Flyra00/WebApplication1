using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Membership;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Tests;

public class PaymentStatusAccessTests
{
    [Fact]
    public async Task Guest_WithoutToken_CannotAccessStatus()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPaidMidtransOrderAsync(db);
        var controller = CreateController(db);

        var result = await controller.Status(seeded.OrderNumber, null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Guest_WithValidTableToken_CanAccessStatus()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPaidMidtransOrderAsync(db);
        var controller = CreateController(db);

        var result = await controller.Status(seeded.OrderNumber, seeded.TableToken);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
    }

    [Fact]
    public async Task Customer_WithDifferentUserId_CannotAccessStatus()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPaidMidtransOrderAsync(db);
        var controller = CreateController(db, CreateUser(AppRoles.Customer, "another-user"));

        var result = await controller.Status(seeded.OrderNumber, null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Customer_WithOwnUserId_CanAccessStatus()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPaidMidtransOrderAsync(db);
        var controller = CreateController(db, CreateUser(AppRoles.Customer, seeded.CustomerUserId));

        var result = await controller.Status(seeded.OrderNumber, null);

        Assert.IsType<JsonResult>(result);
    }

    [Fact]
    public async Task Admin_CanAccessStatus_WithoutToken()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPaidMidtransOrderAsync(db);
        var controller = CreateController(db, CreateUser(AppRoles.Admin, "admin-1"));

        var result = await controller.Status(seeded.OrderNumber, null);

        Assert.IsType<JsonResult>(result);
    }

    private static PaymentController CreateController(AppDbContext context, ClaimsPrincipal? user = null)
    {
        var midtransOptions = Options.Create(new MidtransOptions
        {
            ServerKey = "dummy-server",
            ClientKey = "dummy-client",
            IsProduction = false
        });

        var midtransService = new MidtransService(new HttpClient(), midtransOptions);
        var memberSignupService = new PendingMemberSignupService(
            context,
            midtransService,
            new PasswordHasher<ApplicationUser>(),
            NullLogger<PendingMemberSignupService>.Instance);

        var controller = new PaymentController(context, midtransService, memberSignupService, NullLogger<PaymentController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user ?? new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        return controller;
    }

    private static ClaimsPrincipal CreateUser(string role, string userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        }, "TestAuth");

        return new ClaimsPrincipal(identity);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<(string OrderNumber, string TableToken, string CustomerUserId)> SeedPaidMidtransOrderAsync(AppDbContext db)
    {
        var table = new Table
        {
            Number = 99,
            Capacity = 4,
            QrCodeToken = "TOKEN-99",
            IsActive = true
        };

        var session = new TableSession
        {
            Table = table,
            SessionCode = "SES-TEST-0001",
            GuestType = TableGuestTypes.Member,
            Status = TableSessionStatuses.Open,
            StartTime = DateTime.UtcNow
        };

        var order = new Order
        {
            TableSession = session,
            OrderNumber = "ORD-TEST-0001",
            CustomerUserId = "cust-1",
            Status = OrderStatuses.Paid,
            OrderDate = DateTime.UtcNow,
            Subtotal = 100000,
            Total = 100000
        };

        var payment = new Payment
        {
            Order = order,
            Method = PaymentMethods.Midtrans,
            Status = PaymentStatuses.Paid,
            Amount = 100000,
            ReferenceNumber = order.OrderNumber,
            PaymentDate = DateTime.UtcNow
        };

        db.Tables.Add(table);
        db.TableSessions.Add(session);
        db.Orders.Add(order);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        return (order.OrderNumber, table.QrCodeToken, order.CustomerUserId ?? string.Empty);
    }
}
