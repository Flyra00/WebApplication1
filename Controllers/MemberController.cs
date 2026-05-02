using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Customer)]
    public class MemberController : Controller
    {
        private readonly AppDbContext _context;

        public MemberController(AppDbContext context)
        {
            _context = context;
        }

        // ─── Profil member ───────────────────────────────────────────────────
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

            var profile = await _context.MemberProfiles
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.UserId == userId);

            var recentOrders = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items)
                .Where(o => o.CustomerUserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .Take(10)
                .ToListAsync();

            ViewBag.RecentOrders = recentOrders;
            return View(profile);
        }

        // ─── Daftar jadi member ──────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var existing = await _context.MemberProfiles.FirstOrDefaultAsync(m => m.UserId == userId);

            if (existing != null)
            {
                TempData["Error"] = "Anda sudah terdaftar sebagai member.";
                return RedirectToAction(nameof(Profile));
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string? phone)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var existing = await _context.MemberProfiles.FirstOrDefaultAsync(m => m.UserId == userId);

            if (existing != null)
            {
                TempData["Error"] = "Anda sudah terdaftar sebagai member.";
                return RedirectToAction(nameof(Profile));
            }

            var profile = new MemberProfile
            {
                UserId   = userId,
                Phone    = phone?.Trim(),
                Level    = MemberLevels.Bronze,
                Point    = 0,
                JoinedAt = DateTime.UtcNow
            };

            _context.MemberProfiles.Add(profile);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Selamat! Anda berhasil terdaftar sebagai member Bronze.";
            return RedirectToAction(nameof(Profile));
        }
    }
}
