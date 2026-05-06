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
        private static readonly TimeSpan EmptySessionTimeout = TimeSpan.FromMinutes(5);
        private readonly AppDbContext _context;
        public TableController(AppDbContext context)
        {
            _context = context;
        }

        // ─── Daftar meja ─────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            await ReleaseExpiredOpenSessionsAsync();
            var cutoff = DateTime.UtcNow.Subtract(EmptySessionTimeout);

            var tables = await _context.Tables
                .Include(t => t.Sessions.Where(s =>
                    s.Status == TableSessionStatuses.Open &&
                    s.EndTime == null &&
                    (s.StartTime > cutoff || s.Orders.Any())))
                .ThenInclude(s => s.Orders)
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
                var existing = await _context.Tables
                    .Include(t => t.Sessions.Where(s => s.Status == TableSessionStatuses.Open && s.EndTime == null))
                    .FirstOrDefaultAsync(t => t.Id == table.Id);
                if (existing == null)
                    return NotFound();

                // Jangan biarkan QrCodeToken kosong
                if (string.IsNullOrWhiteSpace(table.QrCodeToken))
                    table.QrCodeToken = GenerateQrToken();

                existing.Number = table.Number;
                existing.Capacity = table.Capacity;
                existing.QrCodeToken = table.QrCodeToken;

                if (existing.IsActive && !table.IsActive)
                {
                    var now = DateTime.UtcNow;
                    foreach (var session in existing.Sessions)
                    {
                        session.Status = TableSessionStatuses.Cancelled;
                        session.EndTime = now;
                    }
                }
                existing.IsActive = table.IsActive;

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Meja {table.Number} berhasil diperbarui.";
                return RedirectToAction(nameof(Index));
            }
            return View(table);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var table = await _context.Tables
                .Include(t => t.Sessions.Where(s => s.Status == TableSessionStatuses.Open && s.EndTime == null))
                .FirstOrDefaultAsync(t => t.Id == id);

            if (table == null)
                return NotFound();

            if (table.IsActive)
            {
                table.IsActive = false;
                var now = DateTime.UtcNow;
                foreach (var session in table.Sessions)
                {
                    session.Status = TableSessionStatuses.Cancelled;
                    session.EndTime = now;
                }
                TempData["Success"] = $"Meja {table.Number} sudah dinonaktifkan.";
            }
            else
            {
                table.IsActive = true;
                TempData["Success"] = $"Meja {table.Number} sudah diaktifkan kembali.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
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

        private async Task ReleaseExpiredOpenSessionsAsync()
        {
            var now = DateTime.UtcNow;
            var cutoff = now.Subtract(EmptySessionTimeout);

            var staleSessions = await _context.TableSessions
                .Where(s =>
                    s.Status == TableSessionStatuses.Open &&
                    s.EndTime == null &&
                    s.StartTime <= cutoff &&
                    !s.Orders.Any())
                .ToListAsync();

            if (staleSessions.Count == 0)
                return;

            foreach (var session in staleSessions)
            {
                session.Status = TableSessionStatuses.Cancelled;
                session.EndTime = now;
            }

            await _context.SaveChangesAsync();
        }
    }
}
