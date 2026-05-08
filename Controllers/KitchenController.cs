using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Time;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Kitchen},{AppRoles.Admin}")]
    public class KitchenController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IBusinessTime _businessTime;

        public KitchenController(AppDbContext context, IBusinessTime businessTime)
        {
            _context = context;
            _businessTime = businessTime;
        }

        // ─── Halaman utama dapur ─────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var orders = await GetActiveOrdersQueryAsync();
            return View(orders);
        }

        // ─── AJAX: ambil semua order aktif (JSON) ────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetActiveOrders()
        {
            var orders = await GetActiveOrdersQueryAsync();

            var result = orders.Select(o => new
            {
                id          = o.Id,
                orderNumber = o.OrderNumber,
                orderDate   = _businessTime.ToBusinessTime(o.OrderDate).ToString("HH:mm"),
                orderType   = o.OrderType,
                status      = o.Status,
                tableNumber = o.TableSession?.Table?.Number ?? 0,
                items       = o.Items.Select(i => new
                {
                    id            = i.Id,
                    productName   = i.Product?.Name ?? "—",
                    qty           = i.Qty,
                    note          = i.Note ?? "",
                    kitchenStatus = i.KitchenStatus
                }).ToList()
            }).ToList();

            return Json(new { success = true, data = result, serverTime = _businessTime.BusinessNow.ToString("HH:mm:ss") });
        }

        // ─── AJAX: update status item dapur ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateItemStatusAjax([FromBody] UpdateItemRequest req)
        {
            if (req == null)
                return Json(new { success = false, error = "Data tidak valid." });

            var item = await _context.OrderItems
                .Include(i => i.Order)
                .FirstOrDefaultAsync(i => i.Id == req.ItemId);

            if (item == null)
                return Json(new { success = false, error = "Item tidak ditemukan." });

            var validStatuses = new[] { KitchenStatuses.Queued, KitchenStatuses.Cooking, KitchenStatuses.Ready, KitchenStatuses.Served };
            if (!validStatuses.Contains(req.KitchenStatus))
                return Json(new { success = false, error = "Status tidak valid." });

            item.KitchenStatus = req.KitchenStatus;

            var allItems = await _context.OrderItems
                .Where(i => i.OrderId == item.OrderId)
                .ToListAsync();

            var order = item.Order;
            ApplyKitchenProgressToOrder(order, allItems);

            await _context.SaveChangesAsync();

            return Json(new
            {
                success       = true,
                newItemStatus = item.KitchenStatus,
                newOrderStatus= order.Status
            });
        }

        // ─── Update status item (form biasa — fallback) ───────────────────────
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

            var allItems = await _context.OrderItems
                .Where(i => i.OrderId == item.OrderId)
                .ToListAsync();

            var order = item.Order;
            ApplyKitchenProgressToOrder(order, allItems);

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

        // ─── Helper ──────────────────────────────────────────────────────────
        private async Task<List<Order>> GetActiveOrdersQueryAsync()
        {
            return await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Where(o =>
                    o.Status != OrderStatuses.Cancelled &&
                    o.Items.Any(i => i.KitchenStatus != KitchenStatuses.Served))
                .OrderBy(o => o.OrderDate)
                .ToListAsync();
        }

        private static void ApplyKitchenProgressToOrder(Order order, IReadOnlyCollection<OrderItem> items)
        {
            if (items.Count == 0)
                return;

            var allServed = items.All(i => i.KitchenStatus == KitchenStatuses.Served);
            var anyInProgress = items.Any(i =>
                i.KitchenStatus == KitchenStatuses.Cooking ||
                i.KitchenStatus == KitchenStatuses.Ready ||
                i.KitchenStatus == KitchenStatuses.Served);

            if (allServed)
            {
                if (!string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                {
                    order.Status = OrderStatuses.Completed;
                }

                return;
            }

            if (anyInProgress && string.Equals(order.Status, OrderStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
            {
                order.Status = OrderStatuses.Processing;
            }
        }

        public sealed class UpdateItemRequest
        {
            public int    ItemId        { get; set; }
            public string KitchenStatus { get; set; } = string.Empty;
        }
    }
}
