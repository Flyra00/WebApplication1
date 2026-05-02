using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;


namespace WebApplication1.Controllers
{
    [Authorize(Roles = AppRoles.Admin)]
    public class TableController : Controller
    {
        private readonly AppDbContext _context;
        public TableController(AppDbContext context)
        {
            _context = context;
        }

        // ─── Daftar meja ─────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var tables = await _context.Tables
                .Include(t => t.Sessions.Where(s => s.Status == TableSessionStatuses.Open))
                .OrderBy(t => t.Number)
                .ToListAsync();
            return View(tables);
        }

        // ─── Tambah meja ─────────────────────────────────────────────────────
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Number,Capacity")]Table table)
        {
            if (table == null)
                return NotFound();

            // Generate unique QR token
            table.QrCodeToken = GenerateQrToken();
            table.IsActive    = true;

            if (ModelState.IsValid)
            {
                await _context.Tables.AddAsync(table);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Meja {table.Number} berhasil ditambahkan.";
                return RedirectToAction(nameof(Index));
            }
            return View(table);
        }

        // ─── Edit meja ───────────────────────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var chair = await _context.Tables.FirstOrDefaultAsync(t => t.Id == id);
            return View(chair);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([Bind("Id,Number,Capacity,QrCodeToken,IsActive")]Table table)
        {
            if (ModelState.IsValid)
            {
                // Jangan biarkan QrCodeToken kosong
                if (string.IsNullOrWhiteSpace(table.QrCodeToken))
                    table.QrCodeToken = GenerateQrToken();

                _context.Tables.Update(table);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Meja {table.Number} berhasil diperbarui.";
                return RedirectToAction(nameof(Index));
            }
            return View(table);
        }

        // ─── Regenerate QR token ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateToken(int id)
        {
            var table = await _context.Tables.FindAsync(id);
            if (table == null) return NotFound();

            table.QrCodeToken = GenerateQrToken();
            await _context.SaveChangesAsync();

            TempData["Success"] = $"QR Token meja {table.Number} berhasil diperbarui.";
            return RedirectToAction(nameof(Index));
        }

        // ─── Hapus meja ──────────────────────────────────────────────────────
        public async Task<IActionResult> Delete(int Id)
        {
            var table = await _context.Tables.FirstOrDefaultAsync(t => t.Id == Id);
            return View(table);
        }

        [HttpPost, ActionName(nameof(Delete))]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int Id)
        {
            var table = await _context.Tables.FirstOrDefaultAsync(t => t.Id == Id);
            if (table == null)
                return NotFound();

            _context.Tables.Remove(table);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Meja berhasil dihapus.";
            return RedirectToAction(nameof(Index));
        }

        // ─── Helper ──────────────────────────────────────────────────────────
        private static string GenerateQrToken()
            => Guid.NewGuid().ToString("N")[..16].ToUpper();
    }
}
