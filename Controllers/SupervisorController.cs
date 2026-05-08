using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Time;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Supervisor},{AppRoles.Admin}")]
    public class SupervisorController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IBusinessTime _businessTime;

        public SupervisorController(AppDbContext context, IBusinessTime businessTime)
        {
            _context = context;
            _businessTime = businessTime;
        }

        public async Task<IActionResult> Index()
        {
            var (todayStartUtc, tomorrowStartUtc) = _businessTime.GetUtcDayRange(_businessTime.BusinessToday);

            var lowIngredients = await _context.Ingredients
                .Where(i => i.Qty <= i.MinimumStock)
                .ToListAsync();

            var recentDamages = await _context.DamageReports
                .Include(d => d.InventoryItem)
                .Include(d => d.ReportedByUser)
                .OrderByDescending(d => d.ReportDate)
                .Take(10)
                .ToListAsync();

            var reservationsToday = await _context.Reservations
                .Include(r => r.Order)
                    .ThenInclude(o => o!.Payments)
                .Where(r => r.CreatedAtUtc >= todayStartUtc && r.CreatedAtUtc < tomorrowStartUtc)
                .ToListAsync();

            var summary = ReservationDashboardSummary.FromReservations(reservationsToday);

            // Stats ringkasan
            ViewBag.TotalIngredients    = await _context.Ingredients.CountAsync();
            ViewBag.TotalInventoryItems = await _context.InventoryItems.CountAsync();
            ViewBag.TodayDamageCount    = await _context.DamageReports
                                              .CountAsync(d => d.ReportDate >= todayStartUtc && d.ReportDate < tomorrowStartUtc);
            ViewBag.PendingDamageCount  = await _context.DamageReports
                                              .CountAsync(d => d.Status == "Open");
            ViewBag.ReservationPendingToday = summary.PendingCount;
            ViewBag.ReservationConfirmedToday = summary.ConfirmedCount;
            ViewBag.ReservationCheckedInToday = summary.CheckedInCount;
            ViewBag.ReservationWithPaidPayment = summary.WithPaidPaymentCount;
            ViewBag.ReservationPaymentPending = summary.PaymentPendingCount;
            ViewBag.ReservationUnpaidRemainder = summary.UnpaidRemainderCount;
            ViewBag.LowIngredients  = lowIngredients;
            ViewBag.RecentDamages   = recentDamages;
            return View();
        }
    }
}
