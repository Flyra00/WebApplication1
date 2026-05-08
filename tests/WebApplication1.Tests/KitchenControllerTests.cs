using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Tests;

public class KitchenControllerTests
{
    [Fact]
    public async Task Index_IncludesPaidOrder_WhenItemsNotYetServed()
    {
        await using var db = CreateDbContext();
        SeedOrder(db, OrderStatuses.Paid, KitchenStatuses.Queued);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Order>>(view.Model);
        Assert.Single(model);
    }

    [Fact]
    public async Task UpdateItemStatusAjax_KeepsPaidOrderStatus_AndRemovesItFromQueue_WhenLastItemServed()
    {
        await using var db = CreateDbContext();
        var seeded = SeedOrder(db, OrderStatuses.Paid, KitchenStatuses.Ready);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.UpdateItemStatusAjax(new KitchenController.UpdateItemRequest
        {
            ItemId = seeded.Item.Id,
            KitchenStatus = KitchenStatuses.Served
        });

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);

        db.ChangeTracker.Clear();
        var updatedOrder = await db.Orders.Include(order => order.Items).SingleAsync();
        Assert.Equal(OrderStatuses.Paid, updatedOrder.Status);
        Assert.Equal(KitchenStatuses.Served, updatedOrder.Items.Single().KitchenStatus);

        var refreshedController = CreateController(db);
        var queueResult = await refreshedController.Index();
        var queueView = Assert.IsType<ViewResult>(queueResult);
        var queueModel = Assert.IsAssignableFrom<IEnumerable<Order>>(queueView.Model);
        Assert.Empty(queueModel);
    }

    private static KitchenController CreateController(AppDbContext db)
    {
        return new KitchenController(db, TestBusinessTime.Create())
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
            .Options;

        return new AppDbContext(options);
    }

    private static (Order Order, OrderItem Item) SeedOrder(AppDbContext db, string orderStatus, string itemStatus)
    {
        var table = new Table
        {
            Number = 8,
            Capacity = 4,
            QrCodeToken = "TOKEN-8",
            IsActive = true
        };

        var session = new TableSession
        {
            Table = table,
            SessionCode = "SES-KITCHEN-1",
            GuestType = TableGuestTypes.Guest,
            Status = TableSessionStatuses.Open,
            StartTime = DateTime.UtcNow
        };

        var product = new Product
        {
            Name = "Sate Ayam",
            Category = "Makanan",
            Price = 35000,
            Stock = 10,
            IsAvailable = true
        };

        var order = new Order
        {
            TableSession = session,
            OrderNumber = $"ORD-KITCHEN-{Guid.NewGuid():N}"[..20],
            OrderDate = DateTime.UtcNow,
            Status = orderStatus,
            OrderType = OrderTypes.DineIn,
            Subtotal = 35000,
            Total = 35000
        };

        var item = new OrderItem
        {
            Order = order,
            Product = product,
            Qty = 1,
            UnitPrice = product.Price,
            LineTotal = product.Price,
            KitchenStatus = itemStatus
        };

        db.Tables.Add(table);
        db.TableSessions.Add(session);
        db.Products.Add(product);
        db.Orders.Add(order);
        db.OrderItems.Add(item);

        return (order, item);
    }
}
