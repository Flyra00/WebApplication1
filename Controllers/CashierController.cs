using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Midtrans;
using WebApplication1.Services.Time;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Kasir},{AppRoles.Admin}")]
    public class CashierController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MidtransService _midtransService;
        private readonly ILogger<CashierController> _logger;
        private readonly IBusinessTime _businessTime;

        public sealed class SubmitNewOrderRequest
        {
            public int TableNumber { get; set; }

            public List<SubmitNewOrderItemRequest> Items { get; set; } = new();
        }

        public sealed class SubmitNewOrderItemRequest
        {
            public int ProductId { get; set; }

            public int Qty { get; set; }
        }

        public sealed class CashierPaymentTokenRequest
        {
            public int OrderId { get; set; }

            public string? PaymentMethod { get; set; }
        }

        public sealed class CashierPaymentSyncRequest
        {
            public int PaymentId { get; set; }

            public string? StatusHint { get; set; }
        }

        public CashierController(AppDbContext context, MidtransService midtransService, ILogger<CashierController> logger, IBusinessTime businessTime)
        {
            _context = context;
            _midtransService = midtransService;
            _logger = logger;
            _businessTime = businessTime;
        }

        // ─── Daftar order hari ini ───────────────────────────────────────────
        public async Task<IActionResult> Index(string? status)
        {
            var (todayStartUtc, tomorrowStartUtc) = _businessTime.GetUtcDayRange(_businessTime.BusinessToday);
            var query = _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Reservation)
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .Where(o => o.OrderDate >= todayStartUtc && o.OrderDate < tomorrowStartUtc);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(o => o.Status == status);

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            var needsSave = false;
            foreach (var o in orders)
            {
                if (o.Status != OrderStatuses.Paid && o.Status != OrderStatuses.Cancelled)
                {
                    if (ReservationBillingHelper.GetPaidTotal(o) >= o.Total)
                    {
                        o.Status = OrderStatuses.Paid;
                        needsSave = true;
                    }
                }
            }
            if (needsSave) await _context.SaveChangesAsync();

            ViewBag.SelectedStatus = status;
            return View(orders);
        }

        // ─── Detail pesanan ──────────────────────────────────────────────────
        public async Task<IActionResult> OrderDetail(int id)
        {
            var order = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Reservation)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payments)
                .Include(o => o.CustomerUser)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            var payment = await _context.Payments
                .Where(p => p.OrderId == id)
                .OrderByDescending(p => p.PaymentDate)
                .FirstOrDefaultAsync();

            var paidTotal = ReservationBillingHelper.GetPaidTotal(order);
            var outstanding = ReservationBillingHelper.GetOutstandingAmount(order);

            if (outstanding <= 0m && order.Status != OrderStatuses.Paid && order.Status != OrderStatuses.Cancelled)
            {
                order.Status = OrderStatuses.Paid;
                await _context.SaveChangesAsync();
            }

            ViewBag.PaidTotal = paidTotal;
            ViewBag.OutstandingAmount = outstanding;
            ViewBag.ReservationDepositAmount = order.Reservation != null ? ReservationBillingHelper.GetDepositAmount(order, order.Reservation) : 0m;

            ViewBag.Payment = payment;
            return View(order);
        }

        // ─── Form pembayaran ─────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ProcessPayment(int id)
        {
            var order = await _context.Orders
                .Include(o => o.TableSession).ThenInclude(s => s.Table)
                .Include(o => o.Reservation)
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            var outstanding = ReservationBillingHelper.GetOutstandingAmount(order);
            if (outstanding <= 0m || order.Status == OrderStatuses.Paid)
            {
                TempData["Error"] = "Pesanan ini sudah dibayar.";
                return RedirectToAction(nameof(OrderDetail), new { id });
            }

            ViewBag.PaidTotal = ReservationBillingHelper.GetPaidTotal(order);
            ViewBag.OutstandingAmount = outstanding;
            ViewBag.MinAmount = GetMinimumCashierPaymentAmount(order, outstanding);

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestPaymentToken([FromBody] CashierPaymentTokenRequest request)
        {
            if (request == null || request.OrderId <= 0)
                return Json(new { success = false, error = "Data pembayaran tidak valid." });

            if (!string.Equals((request.PaymentMethod ?? string.Empty).Trim(), PaymentMethods.Midtrans, StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, error = "Metode pembayaran online tidak valid." });

            if (!_context.Database.IsRelational())
            {
                // no-op; the transaction below still works for in-memory tests
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.Reservation)
                    .Include(o => o.Payments)
                    .Include(o => o.TableSession)
                    .FirstOrDefaultAsync(o => o.Id == request.OrderId);

                if (order == null)
                    return Json(new { success = false, error = "Pesanan tidak ditemukan." });

                var outstanding = ReservationBillingHelper.GetOutstandingAmount(order);
                if (outstanding <= 0m || string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                    return Json(new { success = false, error = "Pesanan ini sudah dibayar." });

                var paymentAmount = GetMinimumCashierPaymentAmount(order, outstanding);

                var isReservationOrder = order.ReservationId.HasValue;
                var paidTotalBefore = ReservationBillingHelper.GetPaidTotal(order);
                var purpose = PaymentPurpose.OrderRegular;
                if (isReservationOrder)
                {
                    purpose = paidTotalBefore <= 0m && paymentAmount < order.Total
                        ? PaymentPurpose.ReservationDeposit
                        : PaymentPurpose.ReservationFull;
                }

                var now = DateTime.UtcNow;
                var existingPayment = order.Payments.FirstOrDefault(payment =>
                    payment.Method == PaymentMethods.Midtrans &&
                    payment.Status == PaymentStatuses.Pending &&
                    string.Equals(payment.Purpose, purpose, StringComparison.OrdinalIgnoreCase));

                var referenceNumber = existingPayment?.ReferenceNumber;
                if (string.IsNullOrWhiteSpace(referenceNumber))
                {
                    referenceNumber = await GenerateUniqueReferenceNumberAsync(order, purpose);
                }

                if (existingPayment == null)
                {
                    existingPayment = new Payment
                    {
                        OrderId = order.Id,
                        Method = PaymentMethods.Midtrans,
                        Purpose = purpose,
                        Amount = paymentAmount,
                        PaymentDate = now,
                        Status = PaymentStatuses.Pending,
                        ReferenceNumber = referenceNumber,
                        PaidByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    };
                    _context.Payments.Add(existingPayment);
                }
                else
                {
                    existingPayment.Amount = paymentAmount;
                    existingPayment.PaymentDate = now;
                    existingPayment.Status = PaymentStatuses.Pending;
                    existingPayment.Purpose = purpose;
                    existingPayment.ReferenceNumber = referenceNumber;
                    existingPayment.PaidByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                }

                await _context.SaveChangesAsync();

                var customerName = !string.IsNullOrWhiteSpace(order.GuestName)
                    ? order.GuestName
                    : $"Kasir {User.Identity?.Name ?? string.Empty}".Trim();

                var snapResult = await _midtransService.CreateSnapTransactionAsync(
                    referenceNumber,
                    paymentAmount,
                    customerName,
                    null,
                    !string.IsNullOrWhiteSpace(order.GuestPhone) ? order.GuestPhone : null);

                if (!snapResult.Success || string.IsNullOrWhiteSpace(snapResult.Token))
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, error = snapResult.ErrorMessage ?? "Gagal membuat token pembayaran online." });
                }

                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    paymentId = existingPayment.Id,
                    orderNumber = order.OrderNumber,
                    referenceNumber,
                    amount = paymentAmount,
                    paymentMethod = PaymentMethods.Midtrans,
                    snapToken = snapResult.Token,
                    midtransClientKey = _midtransService.ClientKey,
                    midtransIsProduction = _midtransService.IsProduction
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Cashier Midtrans token request failed for order {OrderId}", request.OrderId);
                return Json(new { success = false, error = "Gagal menyiapkan pembayaran online." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncPaymentStatus([FromBody] CashierPaymentSyncRequest request)
        {
            if (request == null || request.PaymentId <= 0)
                return Json(new { success = false, error = "Data sinkronisasi tidak valid." });

            var payment = await _context.Payments
                .Include(p => p.Order)
                    .ThenInclude(o => o!.Reservation)
                .Include(p => p.Order)
                    .ThenInclude(o => o!.Payments)
                .FirstOrDefaultAsync(p => p.Id == request.PaymentId && p.Method == PaymentMethods.Midtrans);

            if (payment?.Order == null)
                return Json(new { success = false, error = "Pembayaran tidak ditemukan." });

            var referenceNumber = payment.ReferenceNumber?.Trim();
            if (string.IsNullOrWhiteSpace(referenceNumber))
                return Json(new { success = false, error = "Referensi pembayaran tidak valid." });

            var statusDocument = await _midtransService.GetTransactionStatusAsync(referenceNumber);
            if (statusDocument == null)
            {
                if (IsLocalHostRequest())
                {
                    var fallbackStatus = MapMidtransStatus(request.StatusHint, null);
                    ApplyMidtransPaymentStatus(payment, fallbackStatus);
                    RefreshCashierOrderStatus(payment.Order);
                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = true,
                        synced = true,
                        fallback = true,
                        paymentId = payment.Id,
                        paymentStatus = payment.Status,
                        orderStatus = payment.Order.Status,
                        isPaid = string.Equals(payment.Status, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase)
                    });
                }

                return Json(new { success = false, error = "Status Midtrans belum dapat diambil." });
            }

            using var document = statusDocument;
            var root = document.RootElement;
            var mappedStatus = MapMidtransStatus(
                root.TryGetProperty("transaction_status", out var transactionStatusElement) ? transactionStatusElement.GetString() : null,
                root.TryGetProperty("fraud_status", out var fraudStatusElement) ? fraudStatusElement.GetString() : null);

            ApplyMidtransPaymentStatus(payment, mappedStatus);
            RefreshCashierOrderStatus(payment.Order);

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                synced = true,
                paymentId = payment.Id,
                paymentStatus = payment.Status,
                orderStatus = payment.Order.Status,
                isPaid = string.Equals(payment.Status, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase),
                fallback = false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyMidtransPayment(int paymentId)
        {
            var payment = await _context.Payments
                .Include(p => p.Order)
                    .ThenInclude(o => o!.Reservation)
                .Include(p => p.Order)
                    .ThenInclude(o => o!.Payments)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.Method == PaymentMethods.Midtrans);

            if (payment?.Order == null)
            {
                TempData["Error"] = "Pembayaran tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }

            var orderId = payment.OrderId;
            var referenceNumber = payment.ReferenceNumber?.Trim();

            if (string.IsNullOrWhiteSpace(referenceNumber))
            {
                TempData["Error"] = "Referensi pembayaran tidak valid.";
                return RedirectToAction(nameof(OrderDetail), new { id = orderId });
            }

            var statusDocument = await _midtransService.GetTransactionStatusAsync(referenceNumber);
            if (statusDocument == null)
            {
                if (IsLocalHostRequest())
                {
                    ApplyMidtransPaymentStatus(payment, PaymentStatuses.Paid);
                    RefreshCashierOrderStatus(payment.Order);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Simulasi Verifikasi Localhost: Status diubah menjadi Lunas.";
                    return RedirectToAction(nameof(OrderDetail), new { id = orderId });
                }
                TempData["Error"] = "Status Midtrans belum dapat diambil. Pastikan koneksi internet aktif.";
                return RedirectToAction(nameof(OrderDetail), new { id = orderId });
            }

            using var document = statusDocument;
            var root = document.RootElement;
            var mappedStatus = MapMidtransStatus(
                root.TryGetProperty("transaction_status", out var transactionStatusElement) ? transactionStatusElement.GetString() : null,
                root.TryGetProperty("fraud_status", out var fraudStatusElement) ? fraudStatusElement.GetString() : null);

            var changed = ApplyMidtransPaymentStatus(payment, mappedStatus);
            RefreshCashierOrderStatus(payment.Order);

            await _context.SaveChangesAsync();

            if (changed)
            {
                if (payment.Status == PaymentStatuses.Paid)
                    TempData["Success"] = "Verifikasi berhasil. Pembayaran telah Lunas!";
                else
                    TempData["Success"] = $"Verifikasi berhasil. Status saat ini: {payment.Status}";
            }
            else
            {
                TempData["Info"] = $"Status pembayaran di Midtrans masih {payment.Status}.";
            }

            return RedirectToAction(nameof(OrderDetail), new { id = orderId });
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
                    .Include(o => o.Reservation)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null) return NotFound();

                if (order.Status == OrderStatuses.Paid)
                {
                    TempData["Error"] = "Pesanan ini sudah dibayar.";
                    return RedirectToAction(nameof(OrderDetail), new { id = orderId });
                }

                var outstanding = ReservationBillingHelper.GetOutstandingAmount(order);
                var minimumRequired = GetMinimumCashierPaymentAmount(order, outstanding);

                if (amountPaid < minimumRequired)
                {
                    _logger.LogWarning("Cashier payment rejected: insufficient amount for order {OrderId}", orderId);
                    TempData["Error"] = $"Nominal bayar kurang. Minimum: Rp {minimumRequired:N0}";
                    return RedirectToAction(nameof(ProcessPayment), new { id = orderId });
                }

                var normalizedMethod = NormalizeCashierPaymentMethod(method);
                if (normalizedMethod == null)
                {
                    _logger.LogWarning("Cashier payment rejected: invalid method for order {OrderId}", orderId);
                    TempData["Error"] = "Metode pembayaran tidak valid.";
                    return RedirectToAction(nameof(ProcessPayment), new { id = orderId });
                }

                if (string.Equals(normalizedMethod, PaymentMethods.Midtrans, StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Error"] = "Gunakan Bayar Online untuk pembayaran non-tunai.";
                    return RedirectToAction(nameof(ProcessPayment), new { id = orderId });
                }

                var cashierId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var isReservationOrder = order.ReservationId.HasValue;
                var paidTotalBefore = ReservationBillingHelper.GetPaidTotal(order);

                var paymentAmount = outstanding <= 0m
                    ? 0m
                    : Math.Min(amountPaid, outstanding < order.Total ? outstanding : order.Total);

                if (!isReservationOrder)
                {
                    paymentAmount = order.Total;
                }

                var purpose = PaymentPurpose.OrderRegular;
                if (isReservationOrder)
                {
                    purpose = paidTotalBefore <= 0m && paymentAmount < order.Total
                        ? PaymentPurpose.ReservationDeposit
                        : PaymentPurpose.ReservationFull;
                }

                var payment = new Payment
                {
                    OrderId         = orderId,
                    Method          = normalizedMethod,
                    Amount          = paymentAmount,
                    PaymentDate     = DateTime.UtcNow,
                    Status          = PaymentStatuses.Paid,
                    Purpose         = purpose,
                    ReferenceNumber = $"KSR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
                    PaidByUserId    = cashierId
                };

                order.Status = ReservationBillingHelper.GetOutstandingAmount(order) - paymentAmount <= 0m
                    ? OrderStatuses.Paid
                    : OrderStatuses.Submitted;
                _context.Payments.Add(payment);

                await CloseSessionIfFullyPaidAsync(order.TableSessionId, order.Id);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Cashier payment confirmed for order {OrderId} with payment {PaymentId}", orderId, payment.Id);

                TempData["Success"] = $"Pembayaran berhasil! Kembalian: Rp {(amountPaid - paymentAmount):N0}";
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

        private static string MapMidtransStatus(string? transactionStatus, string? fraudStatus)
        {
            var status = (transactionStatus ?? string.Empty).Trim().ToLowerInvariant();
            var fraud = (fraudStatus ?? string.Empty).Trim().ToLowerInvariant();

            if (status == "capture")
                return fraud == "accept" ? PaymentStatuses.Paid : PaymentStatuses.Pending;

            return status switch
            {
                "settlement" => PaymentStatuses.Paid,
                "pending" => PaymentStatuses.Pending,
                "deny" => PaymentStatuses.Failed,
                "cancel" => PaymentStatuses.Failed,
                "expire" => PaymentStatuses.Failed,
                "failure" => PaymentStatuses.Failed,
                _ => PaymentStatuses.Pending
            };
        }

        private static int GetStatusRank(string status)
        {
            if (string.Equals(status, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(status, PaymentStatuses.Failed, StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(status, PaymentStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
        }

        private static bool ApplyMidtransPaymentStatus(Payment payment, string mappedStatus)
        {
            var currentRank = GetStatusRank(payment.Status);
            var incomingRank = GetStatusRank(mappedStatus);

            if (incomingRank < currentRank)
                return false;

            var changed = !string.Equals(payment.Status, mappedStatus, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                payment.Status = mappedStatus;
                payment.PaymentDate = DateTime.UtcNow;
            }

            return changed;
        }

        private static void RefreshCashierOrderStatus(Order order)
        {
            if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var paidTotal = ReservationBillingHelper.GetPaidTotal(order);
            if (paidTotal >= order.Total)
            {
                order.Status = OrderStatuses.Paid;
            }
            else if (string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                // Downgrade if it was Paid but now isn't fully paid
                order.Status = OrderStatuses.Submitted;
            }
        }

        private bool IsLocalHostRequest()
        {
            var host = HttpContext?.Request.Host.Host ?? string.Empty;
            return host.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> GenerateUniqueReferenceNumberAsync(Order order, string purpose)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var shortPurpose = purpose switch
                {
                    PaymentPurpose.ReservationDeposit => "DP",
                    PaymentPurpose.ReservationFull => "FULL",
                    _ => "ORD"
                };
                var candidate = $"KSR-{order.Id}-{shortPurpose}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

                var exists = await _context.Payments.AnyAsync(payment => payment.ReferenceNumber == candidate);
                if (!exists)
                    return candidate;
            }

            throw new InvalidOperationException("Gagal membuat referensi pembayaran yang unik.");
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitNewOrder([FromBody] SubmitNewOrderRequest request)
        {
            if (request == null || request.TableNumber <= 0)
                return Json(new { success = false, error = "Nomor meja tidak valid." });

            var items = request.Items
                .Where(item => item.ProductId > 0 && item.Qty > 0)
                .GroupBy(item => item.ProductId)
                .Select(group => new SubmitNewOrderItemRequest
                {
                    ProductId = group.Key,
                    Qty = group.Sum(item => item.Qty)
                })
                .ToList();

            if (items.Count == 0)
                return Json(new { success = false, error = "Pesanan masih kosong." });

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                await ReleaseExpiredOpenSessionsAsync();

                var table = await _context.Tables.FirstOrDefaultAsync(t => t.Number == request.TableNumber && t.IsActive);
                if (table == null)
                    return Json(new { success = false, error = "Meja tidak ditemukan atau tidak aktif." });

                var productIds = items.Select(item => item.ProductId).OrderBy(id => id).ToList();
                var products = await _context.Products
                    .Where(product => productIds.Contains(product.Id))
                    .OrderBy(product => product.Id)
                    .ToListAsync();

                if (products.Count != productIds.Count)
                    return Json(new { success = false, error = "Ada menu yang tidak ditemukan atau sudah dihapus." });

                foreach (var item in items)
                {
                    var product = products.First(product => product.Id == item.ProductId);
                    if (!product.IsAvailable)
                        return Json(new { success = false, error = $"Menu {product.Name} sedang tidak tersedia." });

                    if (product.Stock < item.Qty)
                        return Json(new { success = false, error = $"Stok {product.Name} tidak cukup. Sisa stok: {product.Stock}." });
                }

                var openSession = await _context.TableSessions
                    .Where(session =>
                        session.TableId == table.Id &&
                        session.Status == TableSessionStatuses.Open &&
                        session.EndTime == null)
                    .FirstOrDefaultAsync();

                if (openSession == null)
                {
                    openSession = new TableSession
                    {
                        TableId = table.Id,
                        SessionCode = await GenerateUniqueCodeAsync("SES"),
                        GuestType = TableGuestTypes.Guest,
                        StartTime = DateTime.UtcNow,
                        Status = TableSessionStatuses.Open
                    };

                    _context.TableSessions.Add(openSession);
                }

                var order = new Order
                {
                    TableSession = openSession,
                    OrderNumber = await GenerateUniqueCodeAsync("ORD"),
                    OrderDate = DateTime.UtcNow,
                    Status = OrderStatuses.Submitted,
                    OrderType = OrderTypes.DineIn
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
                var ppnRate = NormalizePercentage(await GetAppSettingPercentageAsync(AppSettingKeys.OrderPpnPercentage));
                var serviceRate = NormalizePercentage(await GetAppSettingPercentageAsync(AppSettingKeys.OrderServicePercentage));
                order.PpnPercentage = ppnRate > 0 ? ppnRate : null;
                order.ServicePercentage = serviceRate > 0 ? serviceRate : null;
                order.PpnAmount = ppnRate > 0 ? Math.Round(order.Subtotal * (ppnRate / 100m), 2) : 0;
                order.ServiceAmount = serviceRate > 0 ? Math.Round(order.Subtotal * (serviceRate / 100m), 2) : 0;
                order.Total = order.Subtotal + order.PpnAmount + order.ServiceAmount;

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Cashier order created {OrderNumber} for table {TableNumber} with {ItemCount} items", order.OrderNumber, table.Number, order.Items.Sum(item => item.Qty));

                return Json(new
                {
                    success = true,
                    orderNumber = order.OrderNumber,
                    tableNumber = table.Number,
                    orderType = order.OrderType,
                    total = order.Total.ToString("N0", new CultureInfo("id-ID")),
                    itemCount = order.Items.Sum(item => item.Qty)
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Cashier order submission failed");
                return Json(new { success = false, error = "Pesanan gagal diproses. Silakan coba lagi." });
            }
        }

        private async Task CloseSessionIfFullyPaidAsync(int? tableSessionId, int currentOrderId)
        {
            if (!tableSessionId.HasValue || tableSessionId.Value <= 0)
                return;

            var hasUnpaidOrders = await _context.Orders.AnyAsync(o =>
                o.TableSessionId == tableSessionId.Value &&
                o.Id != currentOrderId &&
                o.Status != OrderStatuses.Paid &&
                o.Status != OrderStatuses.Cancelled);

            if (hasUnpaidOrders)
                return;

            var session = await _context.TableSessions.FirstOrDefaultAsync(s =>
                s.Id == tableSessionId.Value &&
                s.Status == TableSessionStatuses.Open &&
                s.EndTime == null);

            if (session == null)
                return;

            session.Status = TableSessionStatuses.Closed;
            session.EndTime = DateTime.UtcNow;
        }

        private async Task ReleaseExpiredOpenSessionsAsync()
        {
            var now = DateTime.UtcNow;
            var cutoff = now.Subtract(TimeSpan.FromMinutes(5));

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
            _logger.LogInformation("Released {Count} stale empty sessions during cashier order submit", staleSessions.Count);
        }

        private async Task<string> GenerateUniqueCodeAsync(string prefix)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var candidate = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

                var exists = prefix == "ORD"
                    ? await _context.Orders.AnyAsync(o => o.OrderNumber == candidate)
                    : await _context.TableSessions.AnyAsync(s => s.SessionCode == candidate);

                if (!exists)
                    return candidate;
            }

            throw new InvalidOperationException("Gagal membuat kode transaksi yang unik.");
        }

        private static decimal NormalizePercentage(decimal? value)
        {
            if (!value.HasValue)
                return 0;

            var normalized = value.Value;
            if (normalized <= 0)
                return 0;
            if (normalized > 100)
                return 100;
            return Math.Round(normalized, 2);
        }

        private async Task<decimal?> GetAppSettingPercentageAsync(string key)
        {
            var rawValue = await _context.AppSettings
                .AsNoTracking()
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(rawValue))
                return null;

            return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static decimal GetMinimumCashierPaymentAmount(Order order, decimal outstanding)
        {
            if (!order.ReservationId.HasValue || order.Reservation == null)
                return outstanding;

            var paidTotal = ReservationBillingHelper.GetPaidTotal(order);
            if (paidTotal > 0m)
                return outstanding;

            var depositAmount = ReservationBillingHelper.GetDepositAmount(order, order.Reservation);
            return Math.Min(depositAmount, order.Total);
        }
    }
}
