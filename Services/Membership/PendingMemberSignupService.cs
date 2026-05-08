using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Services.Membership
{
    public sealed class PendingMemberSignupService
    {
        public const decimal SignupAmount = 300000m;

        private readonly AppDbContext _context;
        private readonly MidtransService _midtrans;
        private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
        private readonly ILogger<PendingMemberSignupService> _logger;

        public PendingMemberSignupService(
            AppDbContext context,
            MidtransService midtrans,
            IPasswordHasher<ApplicationUser> passwordHasher,
            ILogger<PendingMemberSignupService> logger)
        {
            _context = context;
            _midtrans = midtrans;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<PendingMemberSignupCreateResult> CreatePendingSignupAsync(
            string fullName,
            string username,
            string password,
            string phoneNumber,
            CancellationToken cancellationToken = default)
        {
            var normalizedFullName = (fullName ?? string.Empty).Trim();
            var normalizedUsername = (username ?? string.Empty).Trim();
            var normalizedPhone = NormalizePhone(phoneNumber);

            if (string.IsNullOrWhiteSpace(normalizedFullName) || string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(normalizedPhone))
            {
                return PendingMemberSignupCreateResult.Fail("Nama, username, password, dan nomor telepon wajib diisi.");
            }

            var duplicateError = await GetDuplicateErrorAsync(normalizedUsername, normalizedPhone, cancellationToken);
            if (duplicateError != null)
                return PendingMemberSignupCreateResult.Fail(duplicateError);

            if (!_midtrans.IsConfigured)
                return PendingMemberSignupCreateResult.Fail("Konfigurasi Midtrans belum tersedia.");

            var now = DateTime.UtcNow;
            var signupCode = await GenerateUniqueSignupCodeAsync(cancellationToken);
            var tempHash = _passwordHasher.HashPassword(new ApplicationUser
            {
                UserName = normalizedUsername,
                PhoneNumber = normalizedPhone,
                FullName = normalizedFullName
            }, password);

            var signup = new PendingMemberSignup
            {
                SignupCode = signupCode,
                FullName = normalizedFullName,
                Username = normalizedUsername,
                PasswordHashTemp = tempHash,
                PhoneNumber = normalizedPhone,
                Status = PendingMemberSignupStatuses.PendingPayment,
                Amount = SignupAmount,
                MidtransReference = signupCode,
                CreatedAtUtc = now
            };

            _context.PendingMemberSignups.Add(signup);
            await _context.SaveChangesAsync(cancellationToken);

            var snap = await _midtrans.CreateSnapTransactionAsync(
                signup.SignupCode,
                SignupAmount,
                normalizedFullName,
                null,
                normalizedPhone,
                cancellationToken);

            if (!snap.Success || string.IsNullOrWhiteSpace(snap.Token))
            {
                signup.Status = PendingMemberSignupStatuses.Failed;
                signup.FailedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogWarning("Failed to create Midtrans snap token for member signup {SignupCode}", signup.SignupCode);
                return PendingMemberSignupCreateResult.Fail(snap.ErrorMessage ?? "Gagal membuat token pembayaran member.");
            }

            await _context.SaveChangesAsync(cancellationToken);
            return PendingMemberSignupCreateResult.Ok(signup, snap.Token, snap.RedirectUrl);
        }

        public async Task<PendingMemberSignupFinalizeResult> ProcessPaymentStatusAsync(
            string referenceOrCode,
            string mappedStatus,
            CancellationToken cancellationToken = default)
        {
            var normalizedReference = (referenceOrCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedReference))
                return PendingMemberSignupFinalizeResult.Fail("Referensi pendaftaran tidak valid.");

            var signup = await _context.PendingMemberSignups.FirstOrDefaultAsync(
                item => item.SignupCode == normalizedReference || item.MidtransReference == normalizedReference,
                cancellationToken);

            if (signup == null)
                return PendingMemberSignupFinalizeResult.Fail("Pendaftaran member tidak ditemukan.");

            if (string.Equals(signup.Status, PendingMemberSignupStatuses.Activated, StringComparison.OrdinalIgnoreCase))
            {
                return PendingMemberSignupFinalizeResult.Ok(signup, null, alreadyActivated: true);
            }

            if (string.Equals(mappedStatus, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                return await MarkWaitingVerificationAsync(signup, cancellationToken);
            }

            if (string.Equals(mappedStatus, PaymentStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsFinalPaidStatus(signup.Status))
                {
                    signup.Status = PendingMemberSignupStatuses.Failed;
                    signup.FailedAtUtc = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                    return PendingMemberSignupFinalizeResult.Fail("Pembayaran pendaftaran member gagal.");
                }

                return PendingMemberSignupFinalizeResult.Pending(signup);
            }

            if (IsFinalPaidStatus(signup.Status))
            {
                return PendingMemberSignupFinalizeResult.Pending(signup);
            }

            if (!string.Equals(signup.Status, PendingMemberSignupStatuses.PendingPayment, StringComparison.OrdinalIgnoreCase))
            {
                signup.Status = PendingMemberSignupStatuses.PendingPayment;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return PendingMemberSignupFinalizeResult.Pending(signup);
        }

        public async Task<PendingMemberSignupFinalizeResult> VerifyPendingSignupAsync(int signupId, CancellationToken cancellationToken = default)
        {
            var signup = await _context.PendingMemberSignups.FirstOrDefaultAsync(item => item.Id == signupId, cancellationToken);
            if (signup == null)
                return PendingMemberSignupFinalizeResult.Fail("Pendaftaran member tidak ditemukan.");

            if (string.Equals(signup.Status, PendingMemberSignupStatuses.Activated, StringComparison.OrdinalIgnoreCase))
                return PendingMemberSignupFinalizeResult.Ok(signup, null, alreadyActivated: true);

            if (!string.Equals(signup.Status, PendingMemberSignupStatuses.WaitingVerification, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(signup.Status, PendingMemberSignupStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                return PendingMemberSignupFinalizeResult.Fail("Pendaftaran belum menunggu verifikasi.");
            }

            return await ActivateSignupAsync(signup, cancellationToken);
        }

        private async Task<PendingMemberSignupFinalizeResult> MarkWaitingVerificationAsync(PendingMemberSignup signup, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            signup.Status = PendingMemberSignupStatuses.WaitingVerification;
            signup.PaidAtUtc ??= now;
            signup.MidtransReference ??= signup.SignupCode;

            await _context.SaveChangesAsync(cancellationToken);
            return PendingMemberSignupFinalizeResult.Pending(signup);
        }

        private async Task<PendingMemberSignupFinalizeResult> ActivateSignupAsync(PendingMemberSignup signup, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var duplicateError = await GetVerificationDuplicateErrorAsync(signup, cancellationToken);
            if (duplicateError != null)
                return PendingMemberSignupFinalizeResult.Fail(duplicateError);

            var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            try
            {
                var customerRoleId = await _context.Roles
                    .Where(role => role.NormalizedName == AppRoles.Customer.ToUpperInvariant())
                    .Select(role => role.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(customerRoleId))
                {
                    signup.Status = PendingMemberSignupStatuses.Failed;
                    signup.FailedAtUtc = now;
                    await _context.SaveChangesAsync(cancellationToken);
                    return PendingMemberSignupFinalizeResult.Fail("Role Customer belum tersedia.");
                }

                var normalizedUsername = signup.Username.ToUpperInvariant();
                var existingUser = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(),
                    UserName = signup.Username,
                    NormalizedUserName = normalizedUsername,
                    FullName = signup.FullName,
                    PhoneNumber = signup.PhoneNumber,
                    EmailConfirmed = true,
                    PhoneNumberConfirmed = false,
                    IsActive = true,
                    CreatedAt = now,
                    PasswordHash = signup.PasswordHashTemp,
                    SecurityStamp = Guid.NewGuid().ToString(),
                    ConcurrencyStamp = Guid.NewGuid().ToString()
                };

                _context.Users.Add(existingUser);

                var roleAssigned = await _context.UserRoles.AnyAsync(userRole =>
                    userRole.UserId == existingUser.Id && userRole.RoleId == customerRoleId, cancellationToken);
                if (!roleAssigned)
                {
                    _context.UserRoles.Add(new IdentityUserRole<string>
                    {
                        UserId = existingUser.Id,
                        RoleId = customerRoleId
                    });
                }

                existingUser.FullName = signup.FullName;
                existingUser.PhoneNumber = signup.PhoneNumber;
                existingUser.EmailConfirmed = true;
                existingUser.IsActive = true;
                existingUser.PasswordHash = signup.PasswordHashTemp;
                existingUser.NormalizedUserName = normalizedUsername;

                var existingProfile = await _context.MemberProfiles.FirstOrDefaultAsync(profile => profile.UserId == existingUser.Id, cancellationToken);
                if (existingProfile == null)
                {
                    _context.MemberProfiles.Add(new MemberProfile
                    {
                        UserId = existingUser.Id,
                        Phone = signup.PhoneNumber,
                        Level = MemberLevels.Bronze,
                        Point = 0,
                        JoinedAt = now
                    });
                }
                else
                {
                    existingProfile.Phone = signup.PhoneNumber;
                }

                signup.Status = PendingMemberSignupStatuses.Activated;
                signup.ActivatedAtUtc = now;
                signup.PaidAtUtc ??= now;

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);

                return PendingMemberSignupFinalizeResult.Ok(signup, existingUser, alreadyActivated: false);
            }
            finally
            {
                if (transaction != null)
                    await transaction.DisposeAsync();
            }
        }

        private async Task<string?> GetVerificationDuplicateErrorAsync(PendingMemberSignup signup, CancellationToken cancellationToken)
        {
            var normalizedUsername = signup.Username.ToUpperInvariant();
            var phoneNumber = signup.PhoneNumber;

            var usernameTaken = await _context.Users.AnyAsync(user => user.NormalizedUserName == normalizedUsername, cancellationToken);
            if (usernameTaken)
                return "Username sudah dipakai.";

            var usernameTakenInPending = await GetReservedPendingSignups()
                .Where(item => item.Id != signup.Id)
                .AnyAsync(item => item.Username.ToUpper() == normalizedUsername, cancellationToken);
            if (usernameTakenInPending)
                return "Username sudah dipakai.";

            var phoneTaken = await _context.Users.AnyAsync(user => user.PhoneNumber == phoneNumber, cancellationToken)
                || await _context.MemberProfiles.AnyAsync(profile => profile.Phone == phoneNumber, cancellationToken)
                || await GetReservedPendingSignups()
                    .Where(item => item.Id != signup.Id)
                    .AnyAsync(item => item.PhoneNumber == phoneNumber, cancellationToken);

            if (phoneTaken)
                return "Nomor telepon sudah dipakai.";

            return null;
        }

        private async Task<string?> GetDuplicateErrorAsync(string username, string phoneNumber, CancellationToken cancellationToken)
        {
            var normalizedUsername = username.ToUpperInvariant();

            var usernameTaken = await _context.Users.AnyAsync(user => user.NormalizedUserName == normalizedUsername, cancellationToken);
            if (usernameTaken)
                return "Username sudah dipakai.";

            var usernameTakenInPending = await GetReservedPendingSignups()
                .AnyAsync(signup => signup.Username.ToUpper() == normalizedUsername, cancellationToken);
            if (usernameTakenInPending)
                return "Username sudah dipakai.";

            var phoneTaken = await _context.Users.AnyAsync(user => user.PhoneNumber == phoneNumber, cancellationToken)
                || await _context.MemberProfiles.AnyAsync(profile => profile.Phone == phoneNumber, cancellationToken)
                || await GetReservedPendingSignups()
                    .AnyAsync(signup => signup.PhoneNumber == phoneNumber, cancellationToken);

            if (phoneTaken)
                return "Nomor telepon sudah dipakai.";

            return null;
        }

        private IQueryable<PendingMemberSignup> GetReservedPendingSignups()
        {
            return _context.PendingMemberSignups.Where(signup =>
                signup.Status == PendingMemberSignupStatuses.PendingPayment ||
                signup.Status == PendingMemberSignupStatuses.WaitingVerification ||
                signup.Status == PendingMemberSignupStatuses.Paid ||
                signup.Status == PendingMemberSignupStatuses.Activated);
        }

        private async Task<string> GenerateUniqueSignupCodeAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var code = $"MBR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                var exists = await _context.PendingMemberSignups.AnyAsync(signup => signup.SignupCode == code, cancellationToken);
                if (!exists)
                    return code;
            }

            throw new InvalidOperationException("Gagal membuat kode pendaftaran member yang unik.");
        }

        private static string NormalizePhone(string? phoneNumber)
        {
            var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits;
        }

        private static bool IsReservedSignupStatus(string? status)
        {
            return string.Equals(status, PendingMemberSignupStatuses.PendingPayment, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, PendingMemberSignupStatuses.WaitingVerification, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, PendingMemberSignupStatuses.Paid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, PendingMemberSignupStatuses.Activated, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFinalPaidStatus(string status)
        {
            return string.Equals(status, PendingMemberSignupStatuses.WaitingVerification, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, PendingMemberSignupStatuses.Paid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, PendingMemberSignupStatuses.Activated, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record PendingMemberSignupCreateResult
    {
        public bool IsSuccess { get; init; }
        public string? ErrorMessage { get; init; }
        public PendingMemberSignup? Signup { get; init; }
        public string? SnapToken { get; init; }
        public string? RedirectUrl { get; init; }

        public static PendingMemberSignupCreateResult Ok(PendingMemberSignup signup, string snapToken, string? redirectUrl)
            => new() { IsSuccess = true, Signup = signup, SnapToken = snapToken, RedirectUrl = redirectUrl };

        public static PendingMemberSignupCreateResult Fail(string error)
            => new() { IsSuccess = false, ErrorMessage = error };
    }

    public sealed record PendingMemberSignupFinalizeResult
    {
        public bool IsSuccess { get; init; }
        public bool AlreadyActivated { get; init; }
        public string? ErrorMessage { get; init; }
        public PendingMemberSignup? Signup { get; init; }
        public ApplicationUser? User { get; init; }

        public static PendingMemberSignupFinalizeResult Ok(PendingMemberSignup signup, ApplicationUser? user, bool alreadyActivated)
            => new() { IsSuccess = true, Signup = signup, User = user, AlreadyActivated = alreadyActivated };

        public static PendingMemberSignupFinalizeResult Pending(PendingMemberSignup signup)
            => new() { IsSuccess = true, Signup = signup, AlreadyActivated = false };

        public static PendingMemberSignupFinalizeResult Fail(string error)
            => new() { IsSuccess = false, ErrorMessage = error };
    }
}
