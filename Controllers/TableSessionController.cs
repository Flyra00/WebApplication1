using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [AllowAnonymous]
    public class TableSessionController : Controller
    {
        private static readonly TimeSpan EmptySessionTimeout = TimeSpan.FromMinutes(5);
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<TableSessionController> _logger;

        public TableSessionController(AppDbContext context, UserManager<ApplicationUser> userManager, ILogger<TableSessionController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet("/qr/{token}")]
        public async Task<IActionResult> StartFromQr(string token)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser != null && !await _userManager.IsInRoleAsync(currentUser, AppRoles.Customer))
                {
                    _logger.LogInformation("QR table session ignored for staff user {UserId}", currentUser.Id);
                    return Redirect("/");
                }
            }

            if (string.IsNullOrWhiteSpace(token))
                return NotFound();

            var table = await _context.Tables
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.QrCodeToken == token && t.IsActive);

            if (table == null)
                return NotFound();

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddHours(8),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            };

            Response.Cookies.Append("nr_tableNumber", table.Number.ToString(), cookieOptions);
            Response.Cookies.Append("nr_tableToken", table.QrCodeToken, cookieOptions);

            if (!Request.Cookies.ContainsKey("nr_membershipStatus"))
                Response.Cookies.Append("nr_membershipStatus", "Guest", cookieOptions);

            return Redirect("/#menu");
        }

        [HttpGet("/TableSession/Availability")]
        public async Task<IActionResult> Availability([FromQuery] string? sessionCode = null)
        {
            var cutoff = DateTime.UtcNow.Subtract(EmptySessionTimeout);
            var currentSessionCode = (sessionCode ?? string.Empty).Trim();

            var tables = await _context.Tables
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Number)
                .Select(t => new
                {
                    number = t.Number,
                    capacity = t.Capacity,
                    token = t.QrCodeToken,
                    hasOccupiedSession = _context.TableSessions.Any(s =>
                        s.TableId == t.Id &&
                        s.Status == TableSessionStatuses.Open &&
                        s.EndTime == null &&
                        (s.StartTime > cutoff || s.Orders.Any()) &&
                        s.Orders.Any() &&
                        (string.IsNullOrWhiteSpace(currentSessionCode) || s.SessionCode != currentSessionCode)),
                    hasClaimedSession = _context.TableSessions.Any(s =>
                        s.TableId == t.Id &&
                        s.Status == TableSessionStatuses.Open &&
                        s.EndTime == null &&
                        (s.StartTime > cutoff || s.Orders.Any()) &&
                        !s.Orders.Any() &&
                        (string.IsNullOrWhiteSpace(currentSessionCode) || s.SessionCode != currentSessionCode)),
                    activeSessionCode = _context.TableSessions
                        .Where(s =>
                            s.TableId == t.Id &&
                            s.Status == TableSessionStatuses.Open &&
                            s.EndTime == null &&
                            (s.StartTime > cutoff || s.Orders.Any()))
                        .OrderByDescending(s => s.StartTime)
                        .Select(s => s.SessionCode)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var payload = tables.Select(t => new
            {
                t.number,
                t.capacity,
                t.token,
                isOccupied = t.hasOccupiedSession || t.hasClaimedSession,
                occupancyType = t.hasOccupiedSession ? "Occupied" : t.hasClaimedSession ? "Claimed" : "Available",
                t.activeSessionCode
            });

            return Json(new { success = true, tables = payload });
        }

        public sealed class ClaimTableRequest
        {
            public int? TableNumber { get; set; }

            public string? TableToken { get; set; }

            public string? MembershipStatus { get; set; }
        }

        public sealed class ReleaseTableRequest
        {
            public string? SessionCode { get; set; }
        }

        [HttpPost("/TableSession/Claim")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Claim([FromBody] ClaimTableRequest request)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null && !await _userManager.IsInRoleAsync(user, AppRoles.Customer))
                {
                    _logger.LogWarning("Table claim blocked for staff user {UserId}", user.Id);
                    return Json(new { success = false, error = "Akun staff tidak menggunakan sesi meja." });
                }
            }

            if (request == null)
            {
                _logger.LogWarning("Table claim rejected: invalid payload");
                return Json(new { success = false, error = "Data meja tidak valid." });
            }

            var tableToken = (request.TableToken ?? string.Empty).Trim();
            if (request.TableNumber.GetValueOrDefault() <= 0 && string.IsNullOrWhiteSpace(tableToken))
            {
                _logger.LogWarning("Table claim rejected: invalid table selector");
                return Json(new { success = false, error = "Nomor meja tidak valid." });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            await ReleaseExpiredOpenSessionsAsync();

            var table = !string.IsNullOrWhiteSpace(tableToken)
                ? await _context.Tables.FirstOrDefaultAsync(t => t.QrCodeToken == tableToken && t.IsActive)
                : await _context.Tables.FirstOrDefaultAsync(t => t.Number == request.TableNumber && t.IsActive);

            if (table == null)
            {
                _logger.LogWarning("Table claim rejected: table not found or inactive. Number={TableNumber} Token={TableToken}", request.TableNumber, tableToken);
                return Json(new { success = false, error = "Meja tidak ditemukan atau tidak aktif." });
            }

            var cutoff = DateTime.UtcNow.Subtract(EmptySessionTimeout);

            var existingOpenSession = await _context.TableSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.TableId == table.Id &&
                    s.Status == TableSessionStatuses.Open &&
                    s.EndTime == null &&
                    (s.StartTime > cutoff || s.Orders.Any()));

            if (existingOpenSession != null)
            {
                _logger.LogInformation("Table claim rejected: table {TableNumber} already occupied by session {SessionCode}", table.Number, existingOpenSession.SessionCode);
                return Json(new { success = false, error = "Meja ini sedang dipakai. Silakan pilih meja lain." });
            }

            var guestType = string.Equals(request.MembershipStatus, TableGuestTypes.Member, StringComparison.OrdinalIgnoreCase)
                ? TableGuestTypes.Member
                : TableGuestTypes.Guest;

            var session = new TableSession
            {
                TableId = table.Id,
                SessionCode = await GenerateUniqueSessionCodeAsync(),
                GuestType = guestType,
                MemberUserId = null,
                StartTime = DateTime.UtcNow,
                Status = TableSessionStatuses.Open
            };

            _context.TableSessions.Add(session);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Table claim success: table {TableNumber}, session {SessionCode}, guestType {GuestType}", table.Number, session.SessionCode, session.GuestType);

            return Json(new
            {
                success = true,
                tableNumber = table.Number,
                tableToken = table.QrCodeToken,
                sessionCode = session.SessionCode
            });
        }

        [HttpPost("/TableSession/Release")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Release([FromBody] ReleaseTableRequest request)
        {
            var sessionCode = (request?.SessionCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sessionCode))
                return Json(new { success = true });

            var session = await _context.TableSessions
                .Include(s => s.Orders)
                .FirstOrDefaultAsync(s =>
                    s.SessionCode == sessionCode &&
                    s.Status == TableSessionStatuses.Open &&
                    s.EndTime == null);

            if (session == null)
                return Json(new { success = true });

            if (session.Orders.Count > 0)
                return Json(new { success = false, error = "Sesi sudah memiliki pesanan dan tidak bisa dilepas." });

            session.Status = TableSessionStatuses.Cancelled;
            session.EndTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Table session released manually: {SessionCode}", sessionCode);
            return Json(new { success = true });
        }

        private async Task ReleaseExpiredOpenSessionsAsync()
        {
            var now = DateTime.UtcNow;
            var cutoff = now.Subtract(EmptySessionTimeout);

            var staleSessions = await _context.TableSessions
                .Where(s =>
                    s.Status == TableSessionStatuses.Open &&
                    s.EndTime == null &&
                    s.StartTime <= cutoff &&
                    !s.Orders.Any())
                .ToListAsync();

            if (staleSessions.Count == 0)
                return;

            foreach (var session in staleSessions)
            {
                session.Status = TableSessionStatuses.Cancelled;
                session.EndTime = now;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Released {Count} stale empty table sessions", staleSessions.Count);
        }

        private async Task<string> GenerateUniqueSessionCodeAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var code = $"SES-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                var exists = await _context.TableSessions.AnyAsync(s => s.SessionCode == code);
                if (!exists)
                    return code;
            }

            throw new InvalidOperationException("Gagal membuat kode sesi meja yang unik.");
        }
    }
}
