using System.Globalization;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

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

            public string? TableToken { get; set; }

            public string? MembershipStatus { get; set; }

            public List<SubmitOrderItemRequest> Items { get; set; } = new();
        }

        public sealed class SubmitOrderItemRequest
        {
            public int ProductId { get; set; }

            public int Qty { get; set; }
        }

        [HttpPost("/CustomerOrder/Submit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit([FromBody] SubmitOrderRequest request)
        {
            if (request == null)
            {
                return Json(new { success = false, error = "Data pesanan tidak valid." });
            }

            var tableToken = (request.TableToken ?? string.Empty).Trim();
            if (request.TableNumber <= 0 && string.IsNullOrWhiteSpace(tableToken))
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

            var requestedMembershipStatus = string.Equals(request.MembershipStatus, TableGuestTypes.Member, StringComparison.OrdinalIgnoreCase)
                ? TableGuestTypes.Member
                : TableGuestTypes.Guest;

            var currentUserId = User.IsInRole(AppRoles.Customer)
                ? User.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            var membershipStatus = (requestedMembershipStatus == TableGuestTypes.Member && !string.IsNullOrWhiteSpace(currentUserId))
                ? TableGuestTypes.Member
                : TableGuestTypes.Guest;

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var table = !string.IsNullOrWhiteSpace(tableToken)
                    ? await _context.Tables.FirstOrDefaultAsync(t => t.QrCodeToken == tableToken && t.IsActive)
                    : await _context.Tables.FirstOrDefaultAsync(t => t.Number == request.TableNumber && t.IsActive);

                if (table == null)
                {
                    return Json(new { success = false, error = "Meja tidak ditemukan atau tidak aktif." });
                }

                var productIds = items.Select(item => item.ProductId).OrderBy(id => id).ToList();
                var products = await _context.Products
                    .Where(product => productIds.Contains(product.Id))
                    .OrderBy(product => product.Id)
                    .ToListAsync();

                if (products.Count != productIds.Count)
                {
                    return Json(new { success = false, error = "Ada menu yang tidak ditemukan atau sudah dihapus." });
                }

                foreach (var item in items)
                {
                    var product = products.First(product => product.Id == item.ProductId);
                    if (!product.IsAvailable)
                        return Json(new { success = false, error = $"Menu {product.Name} sedang tidak tersedia." });

                    if (product.Stock < item.Qty)
                        return Json(new { success = false, error = $"Stok {product.Name} tidak cukup. Sisa stok: {product.Stock}." });
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
                    product.Stock -= item.Qty;
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
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    orderNumber = order.OrderNumber,
                    tableNumber = table.Number,
                    total = order.Total.ToString("N0", new CultureInfo("id-ID")),
                    itemCount = order.Items.Sum(item => item.Qty)
                });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, error = "Pesanan gagal diproses. Silakan coba lagi." });
            }
        }

        private static string GenerateCode(string prefix)
        {
            return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
        }
    }
}
