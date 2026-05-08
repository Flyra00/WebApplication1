using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using System.Text;
using WebApplication1.Controllers;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Services.Midtrans;
using WebApplication1.Services.Time;
using WebApplication1.ViewModels.Reservations;

namespace WebApplication1.Tests;

public class ReservationSourcePublicAccessTests
{
    [Fact]
    public async Task PublicCreate_UsesAllowedSlotAndGeneratesAccessKey()
    {
        await using var db = CreateDbContext();
        var businessTime = TestBusinessTime.Create();
        db.Tables.Add(new Table
        {
            Number = 1,
            Capacity = 4,
            QrCodeToken = "TOKEN-1",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, businessTime: businessTime);
        var result = await controller.Create(new ReservationCreateViewModel
        {
            CustomerName = "Budi",
            PhoneNumber = "081234567890",
            ReservationDate = businessTime.BusinessToday.AddDays(1),
            StartTime = "19:00",
            ReservationDurationHours = 2,
            PartySize = 3,
            SpecialRequest = null
        });

        Assert.IsType<RedirectToActionResult>(result);

        var reservation = await db.Reservations.SingleAsync();
        Assert.Equal(ReservationSources.Online, reservation.Source);
        Assert.False(string.IsNullOrWhiteSpace(reservation.AccessKey));
        Assert.Equal(TimeSpan.FromHours(19), businessTime.ToBusinessTime(reservation.ReservationTime).TimeOfDay);
        Assert.Null(reservation.TableId);
    }

    [Fact]
    public async Task PublicCreate_RejectedForInvalidTimeSlot()
    {
        await using var db = CreateDbContext();
        var businessTime = TestBusinessTime.Create();
        db.Tables.Add(new Table
        {
            Number = 1,
            Capacity = 4,
            QrCodeToken = "TOKEN-1",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, businessTime: businessTime);
        var result = await controller.Create(new ReservationCreateViewModel
        {
            CustomerName = "Budi",
            PhoneNumber = "081234567890",
            ReservationDate = businessTime.BusinessToday.AddDays(1),
            StartTime = "19:15",
            ReservationDurationHours = 2,
            PartySize = 2
        });

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await db.Reservations.ToListAsync());
    }

    [Fact]
    public async Task PendingReservation_AllowsOverlappingDuration_UntilTableAssigned()
    {
        await using var db = CreateDbContext();
        var businessTime = TestBusinessTime.Create();
        db.Tables.Add(new Table
        {
            Number = 1,
            Capacity = 4,
            QrCodeToken = "TOKEN-1",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, businessTime: businessTime);

        var first = await controller.Create(new ReservationCreateViewModel
        {
            CustomerName = "Budi",
            PhoneNumber = "081234567890",
            ReservationDate = businessTime.BusinessToday.AddDays(1),
            StartTime = "19:00",
            ReservationDurationHours = 2,
            PartySize = 4
        });

        Assert.IsType<RedirectToActionResult>(first);

        var second = await controller.Create(new ReservationCreateViewModel
        {
            CustomerName = "Ani",
            PhoneNumber = "082233445566",
            ReservationDate = businessTime.BusinessToday.AddDays(1),
            StartTime = "20:00",
            ReservationDurationHours = 1,
            PartySize = 2
        });

        Assert.IsType<RedirectToActionResult>(second);
        Assert.Equal(2, await db.Reservations.CountAsync());
    }

    [Fact]
    public async Task Lookup_PostRedirectsToDetails_ForAccessKey()
    {
        await using var db = CreateDbContext();
        var reservation = new Reservation
        {
            ReservationCode = "RSV-ONLINE-1",
            AccessKey = "AK-ABC123DEF456",
            CustomerName = "Budi",
            PhoneNumber = "081234567890",
            ReservationTime = DateTime.UtcNow.AddHours(2),
            PartySize = 2,
            Status = ReservationStatuses.Pending,
            Source = ReservationSources.Online
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.Lookup(new ReservationLookupViewModel { LookupKey = reservation.AccessKey });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ReservationController.DetailsByCode), redirect.ActionName);
        Assert.Equal(reservation.ReservationCode, redirect.RouteValues!["code"]);
    }

    [Fact]
    public async Task PublicCreate_StoresReservationUsingBusinessTimezone()
    {
        await using var db = CreateDbContext();
        var businessTime = TestBusinessTime.Create(new DateTime(2026, 5, 8, 12, 0, 0));
        db.Tables.Add(new Table
        {
            Number = 2,
            Capacity = 4,
            QrCodeToken = "TOKEN-2",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, businessTime: businessTime);
        var reservationDate = businessTime.BusinessToday.AddDays(1);

        var result = await controller.Create(new ReservationCreateViewModel
        {
            CustomerName = "Citra",
            PhoneNumber = "081200000001",
            ReservationDate = reservationDate,
            StartTime = "10:00",
            ReservationDurationHours = 2,
            PartySize = 2
        });

        Assert.IsType<RedirectToActionResult>(result);

        var reservation = await db.Reservations.SingleAsync();
        Assert.Equal(businessTime.ToUtc(reservationDate.AddHours(10)), reservation.ReservationTime);
    }

    [Fact]
    public async Task Pay_LoadsOrderItemsAndPayments_ForPublicPage()
    {
        await using var db = CreateDbContext();
        var reservation = new Reservation
        {
            ReservationCode = "RSV-ONLINE-2",
            AccessKey = "AK-ABC123DEF789",
            CustomerName = "Budi",
            PhoneNumber = "081234567890",
            ReservationTime = DateTime.UtcNow.AddHours(2),
            PartySize = 2,
            Status = ReservationStatuses.Pending,
            Source = ReservationSources.Online,
            DpPercentage = 50m
        };

        var product = new Product
        {
            Name = "Nasi Goreng",
            Category = "Makanan",
            Price = 50000,
            Stock = 10,
            IsAvailable = true
        };

        db.Reservations.Add(reservation);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var order = new Order
        {
            ReservationId = reservation.Id,
            OrderNumber = "ORD-ONLINE-2",
            Status = OrderStatuses.Submitted,
            OrderType = OrderTypes.DineIn,
            OrderDate = DateTime.UtcNow,
            Subtotal = 50000,
            Total = 50000
        };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            Product = product,
            Qty = 1,
            UnitPrice = product.Price,
            LineTotal = product.Price
        });
        order.Payments.Add(new Payment
        {
            Order = order,
            Method = PaymentMethods.Midtrans,
            Purpose = PaymentPurpose.ReservationDeposit,
            Amount = 25000,
            Status = PaymentStatuses.Pending,
            ReferenceNumber = "RSV-DP-RSV-ONLINE-2-0001"
        });

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.Pay(reservation.ReservationCode);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<Reservation>(view.Model);

        Assert.NotNull(model.Order);
        Assert.Single(model.Order!.Items);
        Assert.Single(model.Order.Payments);
        Assert.Equal(1, model.Order.Items.Sum(item => item.Qty));
    }

    [Fact]
    public async Task Index_FiltersReservations_ByBusinessDateBoundary()
    {
        await using var db = CreateDbContext();
        var businessTime = TestBusinessTime.Create(new DateTime(2026, 5, 8, 12, 0, 0));
        var targetDate = businessTime.BusinessToday.AddDays(1);

        db.Reservations.AddRange(
            new Reservation
            {
                ReservationCode = "RSV-IN-BOUNDARY",
                AccessKey = "AK-BOUNDARY-IN",
                CustomerName = "Boundary In",
                PhoneNumber = "081100000001",
                ReservationTime = businessTime.ToUtc(targetDate.AddMinutes(30)),
                ReservationDurationHours = 2,
                PartySize = 2,
                Status = ReservationStatuses.Pending,
                Source = ReservationSources.Online
            },
            new Reservation
            {
                ReservationCode = "RSV-OUT-BOUNDARY",
                AccessKey = "AK-BOUNDARY-OUT",
                CustomerName = "Boundary Out",
                PhoneNumber = "081100000002",
                ReservationTime = businessTime.ToUtc(targetDate.AddDays(1).AddMinutes(30)),
                ReservationDurationHours = 2,
                PartySize = 2,
                Status = ReservationStatuses.Pending,
                Source = ReservationSources.Online
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, businessTime: businessTime);
        var result = await controller.Index(targetDate, null, null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ReservationFilterViewModel>(view.Model);
        var reservation = Assert.Single(model.Reservations);
        Assert.Equal("RSV-IN-BOUNDARY", reservation.ReservationCode);
    }

    [Fact]
    public async Task SyncPaymentStatus_MarksDepositPaymentPaid_ButKeepsOutstanding()
    {
        await using var db = CreateDbContext();
        var reservation = new Reservation
        {
            ReservationCode = "RSV-ONLINE-3",
            AccessKey = "AK-ABC123DEF999",
            CustomerName = "Sari",
            PhoneNumber = "081234567891",
            ReservationTime = DateTime.UtcNow.AddHours(2),
            PartySize = 4,
            Status = ReservationStatuses.Pending,
            Source = ReservationSources.Online,
            DpPercentage = 50m
        };

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        var order = new Order
        {
            ReservationId = reservation.Id,
            OrderNumber = "ORD-ONLINE-3",
            Status = OrderStatuses.Submitted,
            OrderType = OrderTypes.DineIn,
            OrderDate = DateTime.UtcNow,
            Subtotal = 100000,
            Total = 100000
        };

        var payment = new Payment
        {
            Order = order,
            Method = PaymentMethods.Midtrans,
            Purpose = PaymentPurpose.ReservationDeposit,
            Amount = 50000,
            Status = PaymentStatuses.Pending,
            ReferenceNumber = "RSV-DP-RSV-ONLINE-3-0001"
        };

        db.Orders.Add(order);
        order.Payments.Add(payment);
        await db.SaveChangesAsync();

        var controller = CreateController(db, CreateMidtransService("{\"transaction_status\":\"settlement\",\"fraud_status\":\"accept\"}"));
        var result = await controller.SyncPaymentStatus(new ReservationController.ReservationPaymentSyncRequest
        {
            Code = reservation.ReservationCode,
            OrderId = payment.ReferenceNumber
        });

        var json = Assert.IsType<JsonResult>(result);
        var payload = json.Value!;
        var success = (bool)(payload.GetType().GetProperty("success")?.GetValue(payload) ?? false);
        Assert.True(success);

        db.ChangeTracker.Clear();
        var updatedPayment = await db.Payments.SingleAsync(item => item.Id == payment.Id);
        var updatedOrder = await db.Orders.SingleAsync(item => item.Id == order.Id);

        Assert.Equal(PaymentStatuses.Paid, updatedPayment.Status);
        Assert.Equal(OrderStatuses.Submitted, updatedOrder.Status);
    }

    private static ReservationController CreateController(AppDbContext db, MidtransService? midtransService = null, IBusinessTime? businessTime = null)
    {
        var userManager = CreateUserManager();

        return new ReservationController(
            db,
            userManager,
            NullLogger<ReservationController>.Instance,
            midtransService ?? CreateMidtransService(),
            businessTime ?? TestBusinessTime.Create())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static MidtransService CreateMidtransService(string? responseJson = null)
    {
        var midtransOptions = Options.Create(new MidtransOptions
        {
            ServerKey = "dummy-server",
            ClientKey = "dummy-client",
            IsProduction = false
        });

        return new MidtransService(new HttpClient(responseJson == null ? new PassThroughHandler() : new StaticResponseHandler(responseJson)), midtransOptions);
    }

    private static UserManager<ApplicationUser> CreateUserManager()
    {
        var store = new FakeUserStore();
        var options = Options.Create(new IdentityOptions());
        var passwordHasher = new PasswordHasher<ApplicationUser>();
        var userValidators = Array.Empty<IUserValidator<ApplicationUser>>();
        var passwordValidators = Array.Empty<IPasswordValidator<ApplicationUser>>();
        var keyNormalizer = new UpperInvariantLookupNormalizer();
        var errors = new IdentityErrorDescriber();

        return new UserManager<ApplicationUser>(
            store,
            options,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class PassThroughHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StaticResponseHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
    }
}
