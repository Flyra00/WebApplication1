using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Membership;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Tests;

public class PendingMemberSignupTests
{
    [Fact]
    public async Task CreatePendingSignup_CreatesPendingRecord_WithFixedAmount()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, "{\"token\":\"snap-token-1\",\"redirect_url\":\"https://example.com\"}");

        var result = await service.CreatePendingSignupAsync("Budi", "budi01", "Secret123!", "081234567890");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Signup);
        Assert.Equal(PendingMemberSignupService.SignupAmount, result.Signup!.Amount);
        Assert.Equal(PendingMemberSignupStatuses.PendingPayment, result.Signup.Status);
        Assert.Equal("snap-token-1", result.SnapToken);

        var saved = await db.PendingMemberSignups.SingleAsync();
        Assert.Equal("081234567890", saved.PhoneNumber);
    }

    [Fact]
    public async Task ProcessPaymentStatus_SetsWaitingVerification_AndDoesNotCreateUser()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, "{\"token\":\"snap-token-1\",\"redirect_url\":\"https://example.com\"}");

        var signup = (await service.CreatePendingSignupAsync("Budi", "budi01", "Secret123!", "081234567890")).Signup!;

        var result = await service.ProcessPaymentStatusAsync(signup.SignupCode, PaymentStatuses.Paid);

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingMemberSignupStatuses.WaitingVerification, result.Signup!.Status);
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.MemberProfiles.ToListAsync());
    }

    [Fact]
    public async Task VerifyPendingSignup_CreatesMember_AndIsIdempotent()
    {
        await using var db = CreateDbContext();
        SeedCustomerRole(db);
        var service = CreateService(db, "{\"token\":\"snap-token-1\",\"redirect_url\":\"https://example.com\"}");

        var signup = (await service.CreatePendingSignupAsync("Budi", "budi01", "Secret123!", "081234567890")).Signup!;
        await service.ProcessPaymentStatusAsync(signup.SignupCode, PaymentStatuses.Paid);

        var verifyResult = await service.VerifyPendingSignupAsync(signup.Id);

        Assert.True(verifyResult.IsSuccess);
        Assert.Equal(PendingMemberSignupStatuses.Activated, verifyResult.Signup!.Status);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.MemberProfiles.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());

        var secondVerify = await service.VerifyPendingSignupAsync(signup.Id);
        Assert.True(secondVerify.IsSuccess);
        Assert.True(secondVerify.AlreadyActivated);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.MemberProfiles.ToListAsync());
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Fact]
    public async Task CreatePendingSignup_ReturnsUsernameError_WhenReservedSignupUsesSameUsername()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, "{\"token\":\"snap-token-1\",\"redirect_url\":\"https://example.com\"}");

        db.PendingMemberSignups.Add(new PendingMemberSignup
        {
            SignupCode = "MBR-EXISTING-1",
            FullName = "Existing User",
            Username = "budi01",
            PasswordHashTemp = "hash",
            PhoneNumber = "081111111111",
            Status = PendingMemberSignupStatuses.PendingPayment,
            Amount = PendingMemberSignupService.SignupAmount,
            MidtransReference = "MBR-EXISTING-1",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.CreatePendingSignupAsync("Budi Baru", "budi01", "Secret123!", "082222222222");

        Assert.False(result.IsSuccess);
        Assert.Equal("Username sudah dipakai.", result.ErrorMessage);
    }

    private static PendingMemberSignupService CreateService(AppDbContext db, string responseJson)
    {
        var midtransOptions = Options.Create(new MidtransOptions
        {
            ServerKey = "dummy-server",
            ClientKey = "dummy-client",
            IsProduction = false
        });

        var midtrans = new MidtransService(new HttpClient(new StaticResponseHandler(responseJson)), midtransOptions);
        return new PendingMemberSignupService(db, midtrans, new PasswordHasher<ApplicationUser>(), NullLogger<PendingMemberSignupService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static void SeedCustomerRole(AppDbContext db)
    {
        db.Roles.Add(new IdentityRole
        {
            Id = "role-customer",
            Name = AppRoles.Customer,
            NormalizedName = AppRoles.Customer.ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
        db.SaveChanges();
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
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
