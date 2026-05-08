using System.Globalization;
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

        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var culture = new CultureInfo("id-ID");

            var profile = await _context.MemberProfiles
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.UserId == userId);

            var paidOrders = await _context.Orders
                .Where(o => o.CustomerUserId == userId && o.Status == OrderStatuses.Paid)
                .ToListAsync();

            var stats = new MemberStatsViewModel
            {
                TotalPesanan = paidOrders.Count,
                TotalPengeluaran = paidOrders.Sum(o => o.Total),
                TotalMenu = await _context.OrderItems
                    .Include(i => i.Order)
                    .Where(i => i.Order.CustomerUserId == userId)
                    .SumAsync(i => (int?)i.Qty) ?? 0
            };
            ViewBag.Stats = stats;

            var recentOrders = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Where(o => o.CustomerUserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .Take(20)
                .ToListAsync();
            ViewBag.RecentOrders = recentOrders;

            var frequentlyBoughtIds = await _context.OrderItems
                .Include(i => i.Order)
                .Where(i => i.Order.CustomerUserId == userId)
                .GroupBy(i => i.ProductId)
                .OrderByDescending(g => g.Sum(i => i.Qty))
                .Select(g => g.Key)
                .Take(5)
                .ToListAsync();

            var discountProducts = await _context.Products
                .Where(p => p.IsAvailable && p.Stock > 0 && p.MemberDiscountPercentage > 0)
                .OrderByDescending(p => p.MemberDiscountPercentage)
                .Take(6)
                .ToListAsync();

            var frequentlyBoughtProducts = await _context.Products
                .Where(p => p.IsAvailable && p.Stock > 0 && frequentlyBoughtIds.Contains(p.Id))
                .ToListAsync();

            var frequentRank = frequentlyBoughtIds
                .Select((productId, index) => new { productId, index })
                .ToDictionary(item => item.productId, item => item.index);

            var recommended = frequentlyBoughtProducts
                .OrderBy(product => frequentRank[product.Id])
                .Concat(discountProducts.Where(product => !frequentRank.ContainsKey(product.Id)))
                .Distinct()
                .Take(6)
                .ToList();

            ViewBag.RecommendedProducts = recommended;
            ViewBag.Culture = culture;

            return View(profile);
        }

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

            var phoneDigits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
            if (phoneDigits.Length < 10)
            {
                TempData["Error"] = "Nomor telepon minimal 10 digit.";
                return View();
            }

            var usedPhone = await _context.MemberProfiles
                .AsNoTracking()
                .AnyAsync(m => m.Phone == phoneDigits);
            if (usedPhone)
            {
                TempData["Error"] = "Nomor telepon sudah dipakai member lain.";
                return View();
            }

            var profile = new MemberProfile
            {
                UserId = userId,
                Phone = phoneDigits,
                Level = MemberLevels.Bronze,
                Point = 0,
                JoinedAt = DateTime.UtcNow
            };

            _context.MemberProfiles.Add(profile);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Selamat! Anda berhasil terdaftar sebagai member Bronze.";
            return RedirectToAction(nameof(Profile));
        }
    }

    public class MemberStatsViewModel
    {
        public int TotalPesanan { get; set; }
        public decimal TotalPengeluaran { get; set; }
        public int TotalMenu { get; set; }
    }
}
