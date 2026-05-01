using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [AllowAnonymous]
    public class CustomerOrderController : Controller
    {
        private readonly AppDbContext _context;

        public CustomerOrderController(AppDbContext context)
        {
            _context = context;
        }

        public sealed class SubmitOrderRequest
        {
            public int TableNumber { get; set; }

            public string? MembershipStatus { get; set; }

            public List<SubmitOrderItemRequest> Items { get; set; } = new();
        }

        public sealed class SubmitOrderItemRequest
        {
            public int ProductId { get; set; }

            public int Qty { get; set; }
        }

        [HttpPost("/CustomerOrder/Submit")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Submit([FromBody] SubmitOrderRequest request)
        {
            if (request.TableNumber <= 0)
            {
                return Json(new { success = false, error = "Nomor meja tidak valid." });
            }

            var items = request.Items
                .Where(item => item.ProductId > 0 && item.Qty > 0)
                .GroupBy(item => item.ProductId)
                .Select(group => new SubmitOrderItemRequest
                {
                    ProductId = group.Key,
                    Qty = group.Sum(item => item.Qty)
                })
                .ToList();

            if (items.Count == 0)
            {
                return Json(new { success = false, error = "Pesanan masih kosong." });
            }

            var membershipStatus = string.Equals(request.MembershipStatus, TableGuestTypes.Member, StringComparison.OrdinalIgnoreCase)
                ? TableGuestTypes.Member
                : TableGuestTypes.Guest;

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (membershipStatus == TableGuestTypes.Member && string.IsNullOrWhiteSpace(currentUserId))
            {
                return Json(new { success = false, error = "Silakan login sebagai member sebelum mengirim pesanan." });
            }

            var table = await _context.Tables.FirstOrDefaultAsync(t => t.Number == request.TableNumber);
            if (table == null)
            {
                return Json(new { success = false, error = "Meja tidak ditemukan." });
            }

            var productIds = items.Select(item => item.ProductId).ToList();
            var products = await _context.Products
                .Where(product => productIds.Contains(product.Id))
                .ToListAsync();

            if (products.Count != productIds.Count)
            {
                return Json(new { success = false, error = "Ada menu yang tidak ditemukan atau sudah dihapus." });
            }

            var openSession = await _context.TableSessions
                .FirstOrDefaultAsync(session =>
                    session.TableId == table.Id &&
                    session.Status == TableSessionStatuses.Open &&
                    session.EndTime == null);

            if (openSession == null)
            {
                openSession = new TableSession
                {
                    TableId = table.Id,
                    SessionCode = GenerateCode("SES"),
                    GuestType = membershipStatus,
                    MemberUserId = currentUserId,
                    StartTime = DateTime.UtcNow,
                    Status = TableSessionStatuses.Open
                };

                _context.TableSessions.Add(openSession);
            }
            else if (string.IsNullOrWhiteSpace(openSession.MemberUserId) && !string.IsNullOrWhiteSpace(currentUserId))
            {
                openSession.MemberUserId = currentUserId;
                openSession.GuestType = membershipStatus;
            }

            var order = new Order
            {
                TableSession = openSession,
                OrderNumber = GenerateCode("ORD"),
                CustomerUserId = currentUserId,
                OrderDate = DateTime.UtcNow,
                Status = OrderStatuses.Submitted
            };

            foreach (var item in items)
            {
                var product = products.First(product => product.Id == item.ProductId);
                order.Items.Add(new OrderItem
                {
                    ProductId = product.Id,
                    Qty = item.Qty,
                    UnitPrice = product.Price,
                    LineTotal = product.Price * item.Qty
                });
            }

            order.Subtotal = order.Items.Sum(item => item.LineTotal);
            order.Total = order.Subtotal;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                orderNumber = order.OrderNumber,
                tableNumber = table.Number,
                total = order.Total.ToString("N0", new CultureInfo("id-ID")),
                itemCount = order.Items.Sum(item => item.Qty)
            });
        }

        private static string GenerateCode(string prefix)
        {
            return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
        }
    }
}
