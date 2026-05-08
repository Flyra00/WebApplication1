using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Tests;

public class MemberControllerTests
{
    [Fact]
    public async Task Profile_Recommendations_IncludeFrequentlyBoughtAvailableProducts()
    {
        await using var db = CreateDbContext();
        const string userId = "cust-1";

        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "budi",
            NormalizedUserName = "BUDI",
            FullName = "Budi"
        });

        db.MemberProfiles.Add(new MemberProfile
        {
            UserId = userId,
            Phone = "081234567890",
            Level = MemberLevels.Bronze,
            Point = 0,
            JoinedAt = DateTime.UtcNow
        });

        var frequentProduct = new Product
        {
            Name = "Nasi Goreng",
            Category = "Makanan",
            Price = 25000,
            Stock = 10,
            IsAvailable = true
        };

        var discountProduct = new Product
        {
            Name = "Es Teh",
            Category = "Minuman",
            Price = 8000,
            Stock = 10,
            IsAvailable = true,
            MemberDiscountPercentage = 10
        };

        db.Products.AddRange(frequentProduct, discountProduct);
        await db.SaveChangesAsync();

        var order = new Order
        {
            OrderNumber = "ORD-MEMBER-1",
            CustomerUserId = userId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatuses.Paid,
            OrderType = OrderTypes.DineIn,
            Subtotal = 50000,
            Total = 50000
        };

        order.Items.Add(new OrderItem
        {
            ProductId = frequentProduct.Id,
            Qty = 2,
            UnitPrice = frequentProduct.Price,
            LineTotal = frequentProduct.Price * 2
        });

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId);
        var result = await controller.Profile();

        Assert.IsType<ViewResult>(result);
        var recommendedProducts = Assert.IsAssignableFrom<IEnumerable<Product>>((object?)controller.ViewBag.RecommendedProducts);
        Assert.Contains(recommendedProducts, product => product.Id == frequentProduct.Id);
    }

    private static MemberController CreateController(AppDbContext db, string userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, AppRoles.Customer)
        }, "TestAuth");

        return new MemberController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
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
}
