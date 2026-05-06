using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Kasir},{AppRoles.Admin}")]
    public class CashierController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CashierController> _logger;

        public CashierController(AppDbContext context, ILogger<CashierController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ─── Daftar order hari ini ───────────────────────────────────────────
        public async Task<IActionResult> Index(string? status)
        {
            var today = DateTime.UtcNow.Date;
            var query = _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items)
                .Where(o => o.OrderDate.Date == today);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.SelectedStatus = status;
            return View(orders);
        }

        // ─── Detail pesanan ──────────────────────────────────────────────────
        public async Task<IActionResult> OrderDetail(int id)
        {
            var order = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.CustomerUser)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.OrderId == id && p.Status == PaymentStatuses.Paid);

            ViewBag.Payment = payment;
            return View(order);
        }

        // ─── Form pembayaran ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ProcessPayment(int id)
        {
            var order = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            // Sudah dibayar? Redirect ke detail
            if (order.Status == OrderStatuses.Paid)
            {
                TempData["Error"] = "Pesanan ini sudah dibayar.";
                return RedirectToAction(nameof(OrderDetail), new { id });
            }

            return View(order);
        }

        // ─── Konfirmasi pembayaran ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPayment(int orderId, string method, decimal amountPaid)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.TableSession)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null) return NotFound();

                if (order.Status == OrderStatuses.Paid)
                {
                    TempData["Error"] = "Pesanan ini sudah dibayar.";
                    return RedirectToAction(nameof(OrderDetail), new { id = orderId });
                }

                if (amountPaid < order.Total)
                {
                    _logger.LogWarning("Cashier payment rejected: insufficient amount for order {OrderId}", orderId);
                    TempData["Error"] = $"Nominal bayar kurang. Total: Rp {order.Total:N0}";
                    return RedirectToAction(nameof(ProcessPayment), new { id = orderId });
                }

                var normalizedMethod = NormalizeCashierPaymentMethod(method);
                if (normalizedMethod == null)
                {
                    _logger.LogWarning("Cashier payment rejected: invalid method for order {OrderId}", orderId);
                    TempData["Error"] = "Metode pembayaran tidak valid.";
                    return RedirectToAction(nameof(ProcessPayment), new { id = orderId });
                }

                var cashierId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var payment = new Payment
                {
                    OrderId         = orderId,
                    Method          = normalizedMethod,
                    Amount          = order.Total,
                    PaymentDate     = DateTime.UtcNow,
                    Status          = PaymentStatuses.Paid,
                    ReferenceNumber = $"KSR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
                    PaidByUserId    = cashierId
                };

                order.Status = OrderStatuses.Paid;
                _context.Payments.Add(payment);

                await CloseSessionIfFullyPaidAsync(order.TableSessionId, order.Id);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Cashier payment confirmed for order {OrderId} with payment {PaymentId}", orderId, payment.Id);

                TempData["Success"] = $"Pembayaran berhasil! Kembalian: Rp {(amountPaid - order.Total):N0}";
                return RedirectToAction(nameof(PrintReceipt), new { id = payment.Id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Cashier payment failed for order {OrderId}", orderId);
                TempData["Error"] = "Pembayaran gagal diproses. Silakan coba lagi.";
                return RedirectToAction(nameof(ProcessPayment), new { id = orderId });
            }
        }

        private static string? NormalizeCashierPaymentMethod(string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
                return null;

            var normalized = method.Trim();
            if (string.Equals(normalized, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase))
                return PaymentMethods.Cash;
            if (string.Equals(normalized, PaymentMethods.QRIS, StringComparison.OrdinalIgnoreCase))
                return PaymentMethods.QRIS;
            if (string.Equals(normalized, PaymentMethods.Transfer, StringComparison.OrdinalIgnoreCase))
                return PaymentMethods.Transfer;
            if (string.Equals(normalized, PaymentMethods.Midtrans, StringComparison.OrdinalIgnoreCase))
                return PaymentMethods.Midtrans;

            return null;
        }

        // ─── Cetak struk ─────────────────────────────────────────────────────
        public async Task<IActionResult> PrintReceipt(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Order)
                    .ThenInclude(o => o.Items)
                        .ThenInclude(i => i.Product)
                .Include(p => p.Order)
                    .ThenInclude(o => o.TableSession)
                        .ThenInclude(s => s.Table)
                .Include(p => p.PaidByUser)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();
            return View(payment);
        }

        // ─── Input pesanan kasir (offline) ───────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> NewOrder()
        {
            var tables = await _context.Tables.Where(t => t.IsActive).OrderBy(t => t.Number).ToListAsync();
            var products = await _context.Products.Where(p => p.IsAvailable && p.Stock > 0).OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();
            ViewBag.Tables = tables;
            ViewBag.Products = products;
            return View();
        }

        private async Task CloseSessionIfFullyPaidAsync(int tableSessionId, int currentOrderId)
        {
            var hasUnpaidOrders = await _context.Orders.AnyAsync(o =>
                o.TableSessionId == tableSessionId &&
                o.Id != currentOrderId &&
                o.Status != OrderStatuses.Paid &&
                o.Status != OrderStatuses.Cancelled);

            if (hasUnpaidOrders)
                return;

            var session = await _context.TableSessions.FirstOrDefaultAsync(s =>
                s.Id == tableSessionId &&
                s.Status == TableSessionStatuses.Open &&
                s.EndTime == null);

            if (session == null)
                return;

            session.Status = TableSessionStatuses.Closed;
            session.EndTime = DateTime.UtcNow;
        }
    }
}
