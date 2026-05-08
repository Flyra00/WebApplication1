using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Time;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Owner},{AppRoles.Admin}")]
    public class OwnerController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IBusinessTime _businessTime;

        public OwnerController(AppDbContext context, IBusinessTime businessTime)
        {
            _context = context;
            _businessTime = businessTime;
        }

        // ─── Dashboard ringkasan ─────────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            var today = _businessTime.BusinessToday;
            var (todayStartUtc, tomorrowStartUtc) = _businessTime.GetUtcDayRange(today);
            var monthStartUtc = _businessTime.ToUtc(new DateTime(today.Year, today.Month, 1));

            var todayRevenue = await _context.Payments
                .Where(p => p.Status == PaymentStatuses.Paid && p.PaymentDate >= todayStartUtc && p.PaymentDate < tomorrowStartUtc)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var monthRevenue = await _context.Payments
                .Where(p => p.Status == PaymentStatuses.Paid && p.PaymentDate >= monthStartUtc)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var todayOrders = await _context.Orders
                .CountAsync(o => o.OrderDate >= todayStartUtc && o.OrderDate < tomorrowStartUtc);

            var lowIngredients = await _context.Ingredients
                .Where(i => i.Qty <= i.MinimumStock)
                .CountAsync();

            var recentDamages = await _context.DamageReports
                .Include(d => d.InventoryItem)
                .Include(d => d.ReportedByUser)
                .OrderByDescending(d => d.ReportDate)
                .Take(5)
                .ToListAsync();

            ViewBag.TodayRevenue    = todayRevenue;
            ViewBag.MonthRevenue    = monthRevenue;
            ViewBag.TodayOrders     = todayOrders;
            ViewBag.LowIngredients  = lowIngredients;
            ViewBag.RecentDamages   = recentDamages;

            return View();
        }

        // ─── Laporan penjualan ───────────────────────────────────────────────
        public async Task<IActionResult> SalesReport(DateTime? from, DateTime? to)
        {
            var businessToday = _businessTime.BusinessToday;
            var start = (from ?? businessToday.AddDays(-30)).Date;
            var end = (to ?? businessToday).Date;
            var startUtc = _businessTime.ToUtc(start);
            var endUtc = _businessTime.ToUtc(end.AddDays(1));

            var orders = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Where(o => o.Status == OrderStatuses.Paid && o.OrderDate >= startUtc && o.OrderDate < endUtc)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            var totalRevenue = orders.Sum(o => o.Total);

            // Top 5 produk terlaris
            var topProducts = orders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.Product?.Name ?? "Produk Dihapus")
                .Select(g => new { Name = g.Key, Qty = g.Sum(i => i.Qty), Revenue = g.Sum(i => i.LineTotal) })
                .OrderByDescending(x => x.Qty)
                .Take(5)
                .ToList();

            ViewBag.From         = start;
            ViewBag.To           = end;
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.TopProducts  = topProducts;

            return View(orders);
        }

        // ─── Laporan stok ────────────────────────────────────────────────────
        public async Task<IActionResult> StockReport()
        {
            var ingredients = await _context.Ingredients.OrderBy(i => i.ItemName).ToListAsync();
            var inventory   = await _context.InventoryItems.OrderBy(i => i.Category).ThenBy(i => i.ItemName).ToListAsync();

            ViewBag.Ingredients = ingredients;
            ViewBag.Inventory   = inventory;
            return View();
        }

        // ─── Laporan kerusakan (read-only untuk owner) ───────────────────────
        public async Task<IActionResult> DamageReport()
        {
            var reports = await _context.DamageReports
                .Include(d => d.InventoryItem)
                .Include(d => d.ReportedByUser)
                .OrderByDescending(d => d.ReportDate)
                .ToListAsync();

            return View(reports);
        }
    }
}
