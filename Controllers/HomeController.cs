using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly OrderChargesOptions _orderChargesOptions;

        //ambil data
        public HomeController(ILogger<HomeController> logger, AppDbContext context, IWebHostEnvironment env, IOptions<OrderChargesOptions> orderChargesOptions)
        {
            _logger = logger;
            _context = context;
            _env = env;
            _orderChargesOptions = orderChargesOptions.Value;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _context.Products
                .Where(p => p.IsAvailable && p.Stock > 0)
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Name)
                .ToListAsync();

            var activeTables = await _context.Tables
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Number)
                .ToListAsync();

            ViewBag.ActiveTables = activeTables;
            ViewBag.PpnPercentage = NormalizeChargeRate(await GetAppSettingPercentageAsync(AppSettingKeys.OrderPpnPercentage));
            ViewBag.ServicePercentage = NormalizeChargeRate(await GetAppSettingPercentageAsync(AppSettingKeys.OrderServicePercentage));
            return View(products);
        }

        private async Task<decimal?> GetAppSettingPercentageAsync(string key)
        {
            var rawValue = await _context.AppSettings
                .AsNoTracking()
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(rawValue))
                return null;

            return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static decimal NormalizeChargeRate(decimal? rate)
        {
            if (!rate.HasValue)
                return 0;

            var normalized = rate.Value;
            if (normalized < 0)
                return 0;
            if (normalized > 100)
                return 100;
            return Math.Round(normalized, 2);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // ── Track Order ────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> TrackOrder(string? orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                ViewBag.OrderNumber = "";
                ViewBag.OrderFound = false;
                return View();
            }

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber.Trim());

            ViewBag.OrderNumber = orderNumber.Trim();
            ViewBag.OrderFound = order != null;
            return View(order);
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderStatus(string? orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                return Json(new { found = false });

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber.Trim());

            if (order == null)
                return Json(new { found = false });

            return Json(new
            {
                found = true,
                orderNumber = order.OrderNumber,
                status = order.Status,
                orderType = order.OrderType,
                subtotal = order.Subtotal,
                ppnAmount = order.PpnAmount,
                serviceAmount = order.ServiceAmount,
                total = order.Total,
                orderDate = order.OrderDate.ToString("yyyy-MM-dd HH:mm"),
                items = order.Items.Select(item => new
                {
                    productName = item.Product?.Name ?? "Menu",
                    qty = item.Qty,
                    unitPrice = item.UnitPrice,
                    lineTotal = item.LineTotal,
                    kitchenStatus = item.KitchenStatus
                })
            });
        }
    }
}
