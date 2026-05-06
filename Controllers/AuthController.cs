using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser> _users;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AuthController> _logger;

        private const string RegisterOtpPurpose = "Register";
        private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan OtpResendCooldown = TimeSpan.FromSeconds(60);
        private const int MaxOtpAttempt = 5;

        public AuthController(
            SignInManager<ApplicationUser> signIn,
            UserManager<ApplicationUser> users,
            AppDbContext context,
            IWebHostEnvironment env,
            ILogger<AuthController> logger)    
        {
            _signIn = signIn;
            _users = users;
            _context = context;
            _env = env;
            _logger = logger;
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
            if (existingPhoneProfile)
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

            var existingPhoneProfile = await _context.MemberProfiles
                .AsNoTracking()
                .AnyAsync(m => m.Phone == phoneDigits);
            if (existingPhoneProfile)
                return Json(new { success = false, error = "Nomor telepon sudah dipakai member lain." });

            var otpVerified = await VerifyAndConsumeOtpAsync(phoneDigits, otpCode, RegisterOtpPurpose);
            if (!otpVerified)
                return Json(new { success = false, error = "OTP tidak valid atau sudah kedaluwarsa." });

            var user = new ApplicationUser
            {
                FullName = fullName,
                UserName = username,
                PhoneNumber = phoneDigits,
                EmailConfirmed = true
            };

            var create = await _users.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                var msg = string.Join(", ", create.Errors.Select(e => e.Description));
                return Json(new { success = false, error = msg });
            }

            await _users.AddToRoleAsync(user, AppRoles.Customer);

            var profile = new MemberProfile
            {
                UserId = user.Id,
                Phone = phoneDigits,
                Level = MemberLevels.Bronze,
                Point = 0,
                JoinedAt = DateTime.UtcNow
            };
            _context.MemberProfiles.Add(profile);
            await _context.SaveChangesAsync();

            await _signIn.SignInAsync(user, isPersistent: false);

            return Json(new { success = true, redirectUrl = "/", username = user.UserName, isCustomer = true });
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
