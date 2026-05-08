using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Time;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IBusinessTime _businessTime;

        public AdminController(AppDbContext context, IBusinessTime businessTime)
        {
            _context = context;
            _businessTime = businessTime;
        }

        public async Task<IActionResult> Index()
        {
            var (todayStartUtc, tomorrowStartUtc) = _businessTime.GetUtcDayRange(_businessTime.BusinessToday);

            var reservationsToday = await _context.Reservations
                .Include(r => r.Order)
                    .ThenInclude(o => o!.Payments)
                .Where(r => r.CreatedAtUtc >= todayStartUtc && r.CreatedAtUtc < tomorrowStartUtc)
                .ToListAsync();

            var summary = ReservationDashboardSummary.FromReservations(reservationsToday);

            ViewBag.ReservationPendingToday = summary.PendingCount;
            ViewBag.ReservationConfirmedToday = summary.ConfirmedCount;
            ViewBag.ReservationCheckedInToday = summary.CheckedInCount;
            ViewBag.ReservationWithPaidPayment = summary.WithPaidPaymentCount;
            ViewBag.ReservationPaymentPending = summary.PaymentPendingCount;
            ViewBag.ReservationUnpaidRemainder = summary.UnpaidRemainderCount;

            return View();
        }
    }
}
