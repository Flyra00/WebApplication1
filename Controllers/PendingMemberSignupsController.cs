using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Membership;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Kasir}")]
    public class PendingMemberSignupsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly PendingMemberSignupService _signupService;

        public PendingMemberSignupsController(AppDbContext context, PendingMemberSignupService signupService)
        {
            _context = context;
            _signupService = signupService;
        }

        public async Task<IActionResult> Index()
        {
            var signups = await _context.PendingMemberSignups
                .AsNoTracking()
                .Where(signup =>
                    signup.Status == PendingMemberSignupStatuses.WaitingVerification ||
                    signup.Status == PendingMemberSignupStatuses.Paid)
                .OrderByDescending(signup => signup.PaidAtUtc ?? signup.CreatedAtUtc)
                .ThenByDescending(signup => signup.Id)
                .ToListAsync();

            return View(signups);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(int id)
        {
            var result = await _signupService.VerifyPendingSignupAsync(id);

            if (!result.IsSuccess)
            {
                TempData["Error"] = result.ErrorMessage ?? "Verifikasi member gagal.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = result.AlreadyActivated
                ? "Pendaftaran member sudah aktif sebelumnya."
                : $"Member {result.Signup?.FullName ?? string.Empty} berhasil diverifikasi.";

            return RedirectToAction(nameof(Index));
        }
    }
}
