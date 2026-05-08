using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Membership;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser> _users;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AuthController> _logger;
        private readonly PendingMemberSignupService _memberSignupService;
        private readonly MidtransService _midtrans;

        private const string RegisterOtpPurpose = "Register";
        private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan OtpResendCooldown = TimeSpan.FromSeconds(60);
        private const int MaxOtpAttempt = 5;

        public AuthController(
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users,
            AppDbContext context,
            IWebHostEnvironment env,
            ILogger<AuthController> logger,
            PendingMemberSignupService memberSignupService,
            MidtransService midtrans)
        {
            _signIn = signIn;
            _users = users;
            _context = context;
            _env = env;
            _logger = logger;
            _memberSignupService = memberSignupService;
            _midtrans = midtrans;
        }

        [HttpGet("/Auth")]
        [AllowAnonymous]
        public IActionResult Index()
        {
            // Auth UI lives on the public home page modal.
            return RedirectToAction("Index", "Home");
        }

        public sealed class AjaxLoginRequest
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
        }

        public sealed class AjaxRegisterRequest
        {
            public string? FullName { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? Phone { get; set; }
            public string? OtpCode { get; set; }
        }

        public sealed class SyncMemberSignupRequest
        {
            public string? SignupCode { get; set; }
        }

        public sealed class SendRegisterOtpRequest
        {
            public string? Phone { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendRegisterOtp([FromBody] SendRegisterOtpRequest req)
        {
            var phone = (req.Phone ?? string.Empty).Trim();
            var phoneDigits = new string(phone.Where(char.IsDigit).ToArray());

            if (phoneDigits.Length < 10)
                return Json(new { success = false, error = "Nomor telepon minimal 10 digit." });

            var existingPhoneProfile = await _context.MemberProfiles
                .AsNoTracking()
                .AnyAsync(m => m.Phone == phoneDigits);
            var existingPendingSignup = await _context.PendingMemberSignups
                .AsNoTracking()
                .AnyAsync(signup =>
                    (signup.Status == PendingMemberSignupStatuses.PendingPayment ||
                     signup.Status == PendingMemberSignupStatuses.WaitingVerification ||
                     signup.Status == PendingMemberSignupStatuses.Paid ||
                     signup.Status == PendingMemberSignupStatuses.Activated) &&
                    signup.PhoneNumber == phoneDigits);
            if (existingPhoneProfile || existingPendingSignup)
                return Json(new { success = false, error = "Nomor telepon sudah dipakai member lain." });

            var now = DateTime.UtcNow;
            var latestOtp = await _context.PhoneOtpVerifications
                .Where(otp => otp.Phone == phoneDigits && otp.Purpose == RegisterOtpPurpose)
                .OrderByDescending(otp => otp.CreatedAt)
                .FirstOrDefaultAsync();

            if (latestOtp != null && now - latestOtp.CreatedAt < OtpResendCooldown)
            {
                var remaining = (int)Math.Ceiling((OtpResendCooldown - (now - latestOtp.CreatedAt)).TotalSeconds);
                return Json(new { success = false, error = $"Tunggu {remaining} detik sebelum kirim ulang OTP." });
            }

            var otpCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var otp = new PhoneOtpVerification
            {
                Phone = phoneDigits,
                CodeHash = HashOtpCode(otpCode),
                Purpose = RegisterOtpPurpose,
                CreatedAt = now,
                ExpiresAt = now.Add(OtpLifetime),
                IsUsed = false,
                AttemptCount = 0
            };

            _context.PhoneOtpVerifications.Add(otp);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Register OTP issued for phone {Phone}", MaskPhone(phoneDigits));

            if (_env.IsDevelopment())
            {
                return Json(new { success = true, message = "OTP berhasil dibuat (mode internal).", otpCode });
            }

            return Json(new { success = true, message = "OTP berhasil dibuat." });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AjaxLogin([FromBody] AjaxLoginRequest req)
        {
            var username = (req.Username ?? string.Empty).Trim();
            var password = req.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Json(new { success = false, error = "Username & password wajib." });

            var result = await _signIn.PasswordSignInAsync(username, password, isPersistent: false, lockoutOnFailure: false);
            if (!result.Succeeded)
                return Json(new { success = false, error = "Username atau password salah." });

            var user = await _users.FindByNameAsync(username);
            var redirectUrl = "/";
            var isCustomer = false;
            if (user != null)
            {
                isCustomer = await _users.IsInRoleAsync(user, AppRoles.Customer);
                if (await _users.IsInRoleAsync(user, AppRoles.Admin))
                    redirectUrl = "/Admin";
                else if (await _users.IsInRoleAsync(user, AppRoles.Kasir))
                    redirectUrl = "/Cashier";
                else if (await _users.IsInRoleAsync(user, AppRoles.Kitchen))
                    redirectUrl = "/Kitchen";
                else if (await _users.IsInRoleAsync(user, AppRoles.Supervisor))
                    redirectUrl = "/Supervisor";
                else if (await _users.IsInRoleAsync(user, AppRoles.Owner))
                    redirectUrl = "/Owner/Dashboard";

                if (!isCustomer)
                {
                    var sessionCode = Request.Cookies["nr_tableSessionCode"];
                    await ReleaseClaimedSessionForStaffLoginAsync(sessionCode);

                    Response.Cookies.Delete("nr_tableNumber");
                    Response.Cookies.Delete("nr_tableToken");
                    Response.Cookies.Delete("nr_tableSessionCode");
                    Response.Cookies.Delete("nr_membershipStatus");
                }
            }

            return Json(new { success = true, redirectUrl, username = user?.UserName, isCustomer });
        }

        private async Task ReleaseClaimedSessionForStaffLoginAsync(string? sessionCode)
        {
            var code = (sessionCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
                return;

            var session = await _context.TableSessions
                .Include(s => s.Orders)
                .FirstOrDefaultAsync(s =>
                    s.SessionCode == code &&
                    s.Status == TableSessionStatuses.Open &&
                    s.EndTime == null);

            if (session == null)
                return;

            if (session.Orders.Any())
                return;

            session.Status = TableSessionStatuses.Cancelled;
            session.EndTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AjaxRegister([FromBody] AjaxRegisterRequest req)
        {
            var fullName = (req.FullName ?? string.Empty).Trim();
            var username = (req.Username ?? string.Empty).Trim();
            var password = req.Password ?? string.Empty;
            var phone = (req.Phone ?? string.Empty).Trim();
            var otpCode = (req.OtpCode ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(otpCode))
                return Json(new { success = false, error = "Full name, username, password, nomor telepon, OTP wajib." });

            var phoneDigits = new string(phone.Where(char.IsDigit).ToArray());
            if (phoneDigits.Length < 10)
                return Json(new { success = false, error = "Nomor telepon minimal 10 digit." });

            var existing = await _users.FindByNameAsync(username);
            if (existing != null)
                return Json(new { success = false, error = "Username sudah dipakai." });

            var otpVerified = await VerifyAndConsumeOtpAsync(phoneDigits, otpCode, RegisterOtpPurpose);
            if (!otpVerified)
                return Json(new { success = false, error = "OTP tidak valid atau sudah kedaluwarsa." });

            var signupResult = await _memberSignupService.CreatePendingSignupAsync(fullName, username, password, phoneDigits);
            if (!signupResult.IsSuccess || signupResult.Signup == null || string.IsNullOrWhiteSpace(signupResult.SnapToken))
            {
                return Json(new { success = false, error = signupResult.ErrorMessage ?? "Pendaftaran member gagal." });
            }

            return Json(new
            {
                success = true,
                message = "Pendaftaran berhasil. Lanjutkan pembayaran Rp300.000. Setelah pembayaran berhasil, akun menunggu verifikasi kasir/admin.",
                signupCode = signupResult.Signup.SignupCode,
                snapToken = signupResult.SnapToken,
                midtransClientKey = _midtrans.ClientKey,
                midtransIsProduction = _midtrans.IsProduction,
                amount = PendingMemberSignupService.SignupAmount
            });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncMemberSignupPayment([FromBody] SyncMemberSignupRequest req)
        {
            var signupCode = (req.SignupCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(signupCode))
                return Json(new { success = false, error = "Kode pendaftaran tidak valid." });

            var statusDoc = await _midtrans.GetTransactionStatusAsync(signupCode);
            if (statusDoc == null)
                return Json(new { success = true, synced = false, message = "Status pembayaran belum dapat diambil." });

            using var document = statusDoc;
            var root = document.RootElement;
            var mappedStatus = MapMidtransStatus(
                root.TryGetProperty("transaction_status", out var transactionStatusElement) ? transactionStatusElement.GetString() : null,
                root.TryGetProperty("fraud_status", out var fraudStatusElement) ? fraudStatusElement.GetString() : null);

            var result = await _memberSignupService.ProcessPaymentStatusAsync(signupCode, mappedStatus);
            if (!result.IsSuccess || result.Signup == null)
                return Json(new { success = false, error = result.ErrorMessage ?? "Status pembayaran member gagal disinkronkan." });

            return Json(new
            {
                success = true,
                status = result.Signup.Status,
                waitingVerification = result.Signup.Status == PendingMemberSignupStatuses.WaitingVerification || result.Signup.Status == PendingMemberSignupStatuses.Paid,
                paid = result.Signup.Status == PendingMemberSignupStatuses.WaitingVerification || result.Signup.Status == PendingMemberSignupStatuses.Paid,
                activated = result.Signup.Status == PendingMemberSignupStatuses.Activated,
                alreadyActivated = result.AlreadyActivated,
                username = result.User?.UserName,
                isCustomer = result.Signup.Status == PendingMemberSignupStatuses.Activated,
                message = result.Signup.Status == PendingMemberSignupStatuses.WaitingVerification || result.Signup.Status == PendingMemberSignupStatuses.Paid
                    ? "Pembayaran berhasil. Akun member menunggu verifikasi kasir/admin."
                    : result.Signup.Status == PendingMemberSignupStatuses.PendingPayment
                        ? "Pembayaran masih menunggu konfirmasi Midtrans."
                        : result.Signup.Status == PendingMemberSignupStatuses.Activated
                            ? "Akun member sudah aktif."
                            : "Status pembayaran diperbarui."
            });
        }

        private async Task<bool> VerifyAndConsumeOtpAsync(string phoneDigits, string otpCode, string purpose)
        {
            var now = DateTime.UtcNow;

            var otp = await _context.PhoneOtpVerifications
                .Where(x =>
                    x.Phone == phoneDigits &&
                    x.Purpose == purpose &&
                    !x.IsUsed &&
                    x.ExpiresAt >= now)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (otp == null)
                return false;

            if (otp.AttemptCount >= MaxOtpAttempt)
                return false;

            var isMatch = string.Equals(otp.CodeHash, HashOtpCode(otpCode), StringComparison.Ordinal);
            otp.AttemptCount += 1;

            if (!isMatch)
            {
                await _context.SaveChangesAsync();
                return false;
            }

            otp.IsUsed = true;
            await _context.SaveChangesAsync();
            return true;
        }

        private static string HashOtpCode(string code)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            return Convert.ToHexString(bytes);
        }

        private static string MapMidtransStatus(string? transactionStatus, string? fraudStatus)
        {
            var status = (transactionStatus ?? string.Empty).Trim().ToLowerInvariant();
            var fraud = (fraudStatus ?? string.Empty).Trim().ToLowerInvariant();

            if (status == "capture")
                return fraud == "accept" ? PaymentStatuses.Paid : PaymentStatuses.Pending;

            return status switch
            {
                "settlement" => PaymentStatuses.Paid,
                "pending" => PaymentStatuses.Pending,
                "deny" => PaymentStatuses.Failed,
                "cancel" => PaymentStatuses.Failed,
                "expire" => PaymentStatuses.Failed,
                "failure" => PaymentStatuses.Failed,
                _ => PaymentStatuses.Pending
            };
        }

        private static string MaskPhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone) || phone.Length < 4)
                return "***";
            return $"***{phone[^4..]}";
        }

        [HttpPost("/Auth/Logout")]
        public async Task<IActionResult> Logout()
        {
            // Reset table & membership cookies on logout
            Response.Cookies.Delete("nr_tableNumber");
            Response.Cookies.Delete("nr_tableToken");
            Response.Cookies.Delete("nr_tableSessionCode");
            Response.Cookies.Delete("nr_membershipStatus");

            await _signIn.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }
    }
}
