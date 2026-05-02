using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Kitchen},{AppRoles.Admin}")]
    public class KitchenController : Controller
    {
        private readonly AppDbContext _context;

        public KitchenController(AppDbContext context)
        {
            _context = context;
        }

        // ─── Antrian dapur ───────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var orders = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Where(o => o.Status == OrderStatuses.Submitted || o.Status == OrderStatuses.Processing)
                .OrderBy(o => o.OrderDate)
                .ToListAsync();

            return View(orders);
        }

        // ─── Update status item dapur ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateItemStatus(int itemId, string kitchenStatus)
        {
            var item = await _context.OrderItems
                .Include(i => i.Order)
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item == null) return NotFound();

            var validStatuses = new[] { KitchenStatuses.Queued, KitchenStatuses.Cooking, KitchenStatuses.Ready, KitchenStatuses.Served };
            if (!validStatuses.Contains(kitchenStatus))
                return BadRequest("Status tidak valid.");

            item.KitchenStatus = kitchenStatus;

            // Jika semua item sudah Ready/Served → order jadi Processing/Completed
            var allItems = await _context.OrderItems
                .Where(i => i.OrderId == item.OrderId)
                .ToListAsync();

            var order = item.Order;
            bool allServed  = allItems.All(i => i.KitchenStatus == KitchenStatuses.Served);
            bool anyCooked  = allItems.Any(i => i.KitchenStatus == KitchenStatuses.Cooking || i.KitchenStatus == KitchenStatuses.Ready);

            if (allServed)
                order.Status = OrderStatuses.Completed;
            else if (anyCooked && order.Status == OrderStatuses.Submitted)
                order.Status = OrderStatuses.Processing;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // ─── Print tiket dapur ───────────────────────────────────────────────
        public async Task<IActionResult> PrintKitchenTicket(int id)
        {
            var order = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();
            return View(order);
        }
    }
}
