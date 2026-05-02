using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Supervisor},{AppRoles.Admin}")]
    public class SupervisorController : Controller
    {
        private readonly AppDbContext _context;

        public SupervisorController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var lowIngredients = await _context.Ingredients
                .Where(i => i.Qty <= i.MinimumStock)
                .ToListAsync();

            var recentDamages = await _context.DamageReports
                .Include(d => d.InventoryItem)
                .Include(d => d.ReportedByUser)
                .OrderByDescending(d => d.ReportDate)
                .Take(10)
                .ToListAsync();

            ViewBag.LowIngredients  = lowIngredients;
            ViewBag.RecentDamages   = recentDamages;
            return View();
        }
    }
}
