using System.Diagnostics;
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
            ViewBag.PpnPercentage = NormalizeChargeRate(_orderChargesOptions.PpnPercentage);
            ViewBag.ServicePercentage = NormalizeChargeRate(_orderChargesOptions.ServicePercentage);
            return View(products);
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
    }
}
