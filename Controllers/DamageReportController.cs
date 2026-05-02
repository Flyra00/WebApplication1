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
    public class DamageReportController : Controller
    {
        private readonly AppDbContext _context;

        public DamageReportController(AppDbContext context)
        {
            _context = context;
        }

        // ─── Daftar laporan kerusakan ────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var reports = await _context.DamageReports
                .Include(d => d.InventoryItem)
                .Include(d => d.ReportedByUser)
                .OrderByDescending(d => d.ReportDate)
                .ToListAsync();
            return View(reports);
        }

        // ─── Detail laporan ──────────────────────────────────────────────────
        public async Task<IActionResult> Detail(int id)
        {
            var report = await _context.DamageReports
                .Include(d => d.InventoryItem)
                .Include(d => d.ReportedByUser)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (report == null) return NotFound();
            return View(report);
        }

        // ─── Buat laporan baru ───────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var items = await _context.InventoryItems.OrderBy(i => i.ItemName).ToListAsync();
            ViewBag.InventoryItems = items;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int inventoryItemId, int qty, string? description)
        {
            if (qty <= 0)
            {
                TempData["Error"] = "Jumlah minimal 1.";
                return RedirectToAction(nameof(Create));
            }

            var item = await _context.InventoryItems.FindAsync(inventoryItemId);
            if (item == null)
            {
                TempData["Error"] = "Barang tidak ditemukan.";
                return RedirectToAction(nameof(Create));
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

            var report = new DamageReport
            {
                InventoryItemId  = inventoryItemId,
                Qty              = qty,
                Description      = description,
                ReportedByUserId = userId,
                ReportDate       = DateTime.UtcNow,
                Status           = DamageReportStatuses.Reported
            };

            // Kurangi stok barang
            item.Qty = Math.Max(0, item.Qty - qty);

            _context.DamageReports.Add(report);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Laporan kerusakan berhasil dicatat.";
            return RedirectToAction(nameof(Index));
        }

        // ─── Update status laporan (Admin only) ─────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = AppRoles.Admin)]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            var report = await _context.DamageReports.FindAsync(id);
            if (report == null) return NotFound();

            var validStatuses = new[] { DamageReportStatuses.Reported, DamageReportStatuses.Reviewed, DamageReportStatuses.Resolved };
            if (!validStatuses.Contains(status))
                return BadRequest();

            report.Status = status;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Status laporan diperbarui.";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }
}
