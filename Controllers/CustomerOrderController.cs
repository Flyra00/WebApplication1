using System.Globalization;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Controllers
{
    [AllowAnonymous]
    public class CustomerOrderController : Controller
    {
        private static readonly TimeSpan EmptySessionTimeout = TimeSpan.FromMinutes(5);
        private const int TakeawayTableNumber = -1;
        private const string TakeawayTableToken = "TAKEAWAY-ORDER";
        private readonly AppDbContext _context;
        private readonly MidtransService _midtransService;
        private readonly OrderChargesOptions _orderChargesOptions;
        private readonly ILogger<CustomerOrderController> _logger;

        public CustomerOrderController(
            AppDbContext context,
            MidtransService midtransService,
            IOptions<OrderChargesOptions> orderChargesOptions,
            ILogger<CustomerOrderController> logger)
        {
            _context = context;
            _midtransService = midtransService;
            _orderChargesOptions = orderChargesOptions.Value;
            _logger = logger;
        }

        public sealed class SubmitOrderRequest
        {
            public int TableNumber { get; set; }

            public string? TableToken { get; set; }

            public string? SessionCode { get; set; }

            public string? MembershipStatus { get; set; }

            public string? PaymentMethod { get; set; }

            public string? OrderType { get; set; }

            public string? GuestName { get; set; }

            public string? GuestPhone { get; set; }

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
            if (User?.Identity?.IsAuthenticated == true && !User.IsInRole(AppRoles.Customer))
            {
                _logger.LogWarning("Customer submit blocked for non-customer role");
                return Json(new { success = false, error = "Akun staff tidak memesan dari menu pelanggan." });
            }

            if (request == null)
            {
                return Json(new { success = false, error = "Data pesanan tidak valid." });
            }

            var tableToken = (request.TableToken ?? string.Empty).Trim();
            var normalizedOrderType = string.Equals(request.OrderType, OrderTypes.Takeaway, StringComparison.OrdinalIgnoreCase)
                ? OrderTypes.Takeaway
                : OrderTypes.DineIn;

            if (normalizedOrderType == OrderTypes.DineIn && request.TableNumber <= 0 && string.IsNullOrWhiteSpace(tableToken))
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

            var normalizedPaymentMethod = string.Equals(request.PaymentMethod, PaymentMethods.Midtrans, StringComparison.OrdinalIgnoreCase)
                ? PaymentMethods.Midtrans
                : PaymentMethods.Cash;

            if (normalizedPaymentMethod == PaymentMethods.Midtrans && !_midtransService.IsConfigured)
            {
                return Json(new { success = false, error = "Pembayaran online belum tersedia saat ini." });
            }

            var principal = HttpContext?.User;
            var isCustomerUser = principal?.Identity?.IsAuthenticated == true && principal.IsInRole(AppRoles.Customer);
            var currentUserId = isCustomerUser
                ? principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                : null;

            var membershipStatus = (requestedMembershipStatus == TableGuestTypes.Member && !string.IsNullOrWhiteSpace(currentUserId))
                ? TableGuestTypes.Member
                : TableGuestTypes.Guest;

            var isGuestOrder = string.IsNullOrWhiteSpace(currentUserId) && membershipStatus == TableGuestTypes.Guest;

            if (isGuestOrder)
            {
                if (string.IsNullOrWhiteSpace(request.GuestName))
                {
                    return Json(new { success = false, error = "Nama wajib diisi untuk pemesan umum." });
                }

                if (string.IsNullOrWhiteSpace(request.GuestPhone))
                {
                    return Json(new { success = false, error = "Nomor telepon wajib diisi untuk pemesan umum." });
                }

                var phoneDigits = new string(request.GuestPhone.Where(char.IsDigit).ToArray());
                if (phoneDigits.Length < 10)
                {
                    return Json(new { success = false, error = "Nomor telepon minimal 10 digit." });
                }
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                await ReleaseExpiredOpenSessionsAsync();

                var table = normalizedOrderType == OrderTypes.Takeaway
                    ? await GetOrCreateTakeawayTableAsync()
                    : !string.IsNullOrWhiteSpace(tableToken)
                        ? await _context.Tables.FirstOrDefaultAsync(t => t.QrCodeToken == tableToken && t.IsActive)
                        : await _context.Tables.FirstOrDefaultAsync(t => t.Number == request.TableNumber && t.IsActive);

                if (table == null)
                {
                    _logger.LogWarning("Submit order failed: table not found or inactive. Number={TableNumber} Token={TableToken}", request.TableNumber, tableToken);
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

                TableSession openSession;
                if (normalizedOrderType == OrderTypes.Takeaway)
                {
                    openSession = new TableSession
                    {
                        TableId = table.Id,
                        SessionCode = await GenerateUniqueCodeAsync("SES"),
                        GuestType = membershipStatus,
                        MemberUserId = currentUserId,
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        Status = TableSessionStatuses.Closed
                    };
                    _context.TableSessions.Add(openSession);
                }
                else
                {
                    var cutoff = DateTime.UtcNow.Subtract(EmptySessionTimeout);
                    openSession = await _context.TableSessions
                        .Where(session =>
                            session.TableId == table.Id &&
                            session.Status == TableSessionStatuses.Open &&
                            session.EndTime == null)
                        .FirstOrDefaultAsync(session =>
                            session.StartTime > cutoff || session.Orders.Any()) ?? new TableSession();

                    var incomingSessionCode = (request.SessionCode ?? string.Empty).Trim();
                    if (openSession.Id > 0 && string.IsNullOrWhiteSpace(incomingSessionCode))
                    {
                        _logger.LogInformation("Submit order rejected: occupied table {TableNumber} without session code", table.Number);
                        return Json(new { success = false, error = "Meja ini sedang dipakai. Silakan pilih meja lain." });
                    }

                    if (openSession.Id > 0 && !string.IsNullOrWhiteSpace(incomingSessionCode))
                    {
                        if (!string.Equals(openSession.SessionCode, incomingSessionCode, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Submit order rejected: session mismatch on table {TableNumber}. Incoming={Incoming}, Existing={Existing}", table.Number, incomingSessionCode, openSession.SessionCode);
                            return Json(new { success = false, error = "Meja ini sedang dipakai. Silakan pilih meja lain." });
                        }
                    }

                    if (openSession.Id == 0)
                    {
                        openSession = new TableSession
                        {
                            TableId = table.Id,
                            SessionCode = await GenerateUniqueCodeAsync("SES"),
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
                }

                var order = new Order
                {
                    TableSession = openSession,
                    OrderNumber = await GenerateUniqueCodeAsync("ORD"),
                    CustomerUserId = currentUserId,
                    GuestName = isGuestOrder ? request.GuestName?.Trim() : null,
                    GuestPhone = isGuestOrder && !string.IsNullOrWhiteSpace(request.GuestPhone) 
                        ? new string(request.GuestPhone.Where(char.IsDigit).ToArray()) 
                        : null,
                    OrderDate = DateTime.UtcNow,
                    Status = OrderStatuses.Submitted,
                    OrderType = normalizedOrderType
                };

                var isMember = isCustomerUser && !string.IsNullOrWhiteSpace(currentUserId);

                foreach (var item in items)
                {
                    var product = products.First(product => product.Id == item.ProductId);
                    var unitPrice = product.Price;
                    
                    if (isMember && product.MemberDiscountPercentage.HasValue && product.MemberDiscountPercentage > 0)
                    {
                        unitPrice = product.Price * (1 - product.MemberDiscountPercentage.Value / 100);
                    }
                    
                    product.Stock -= item.Qty;
                    order.Items.Add(new OrderItem
                    {
                        ProductId = product.Id,
                        Qty = item.Qty,
                        UnitPrice = unitPrice,
                        LineTotal = unitPrice * item.Qty
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
                _logger.LogInformation("Order created {OrderNumber} for type {OrderType} table {TableNumber} with {ItemCount} items", order.OrderNumber, order.OrderType, table.Number, order.Items.Count);

                if (normalizedPaymentMethod == PaymentMethods.Midtrans)
                {
                    var existingMidtransPayment = await _context.Payments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p =>
                            p.OrderId == order.Id &&
                            p.Method == PaymentMethods.Midtrans);

                    if (existingMidtransPayment != null)
                    {
                        _logger.LogWarning("Duplicate Midtrans payment creation prevented for order {OrderNumber}", order.OrderNumber);
                        await transaction.RollbackAsync();
                        return Json(new { success = false, error = "Pembayaran online untuk pesanan ini sudah dibuat." });
                    }

                    var customerName = !string.IsNullOrWhiteSpace(order.GuestName)
                        ? order.GuestName
                        : !string.IsNullOrWhiteSpace(principal?.Identity?.Name)
                            ? principal.Identity.Name
                            : "Guest";
                    var customerPhone = !string.IsNullOrWhiteSpace(order.GuestPhone)
                        ? order.GuestPhone
                        : !string.IsNullOrWhiteSpace(currentUserId)
                            ? await _context.MemberProfiles.Where(m => m.UserId == currentUserId).Select(m => m.Phone).FirstOrDefaultAsync()
                            : null;
                    
                    var snapResult = await _midtransService.CreateSnapTransactionAsync(
                        order.OrderNumber,
                        order.Total,
                        customerName,
                        null,
                        customerPhone);

                    if (!snapResult.Success || string.IsNullOrWhiteSpace(snapResult.Token))
                    {
                        await transaction.RollbackAsync();
                        return Json(new { success = false, error = "Gagal membuat sesi pembayaran online." });
                    }

                    var payment = new Payment
                    {
                        OrderId = order.Id,
                        Method = PaymentMethods.Midtrans,
                        Amount = order.Total,
                        PaymentDate = DateTime.UtcNow,
                        Status = PaymentStatuses.Pending,
                        ReferenceNumber = order.OrderNumber,
                        PaidByUserId = null
                    };

                    _context.Payments.Add(payment);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Midtrans payment session created for order {OrderNumber}", order.OrderNumber);

                    return Json(new
                    {
                        success = true,
                        orderNumber = order.OrderNumber,
                        tableNumber = normalizedOrderType == OrderTypes.Takeaway ? (int?)null : table.Number,
                        orderType = normalizedOrderType,
                        total = order.Total.ToString("N0", new CultureInfo("id-ID")),
                        itemCount = order.Items.Sum(item => item.Qty),
                        paymentMethod = PaymentMethods.Midtrans,
                        snapToken = snapResult.Token,
                        midtransClientKey = _midtransService.ClientKey,
                        midtransIsProduction = _midtransService.IsProduction
                    });
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Cash order submitted successfully {OrderNumber}", order.OrderNumber);

                return Json(new
                {
                    success = true,
                    orderNumber = order.OrderNumber,
                    tableNumber = normalizedOrderType == OrderTypes.Takeaway ? (int?)null : table.Number,
                    orderType = normalizedOrderType,
                    total = order.Total.ToString("N0", new CultureInfo("id-ID")),
                    itemCount = order.Items.Sum(item => item.Qty)
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Submit order failed with unhandled exception");
                return Json(new { success = false, error = "Pesanan gagal diproses. Silakan coba lagi." });
            }
        }

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
            _logger.LogInformation("Released {Count} stale empty sessions during order submit", staleSessions.Count);
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

        private async Task<Table> GetOrCreateTakeawayTableAsync()
        {
            var existing = await _context.Tables.FirstOrDefaultAsync(t => t.Number == TakeawayTableNumber);
            if (existing != null)
                return existing;

            var table = new Table
            {
                Number = TakeawayTableNumber,
                Capacity = 1,
                QrCodeToken = TakeawayTableToken,
                IsActive = false
            };

            _context.Tables.Add(table);
            await _context.SaveChangesAsync();
            return table;
        }
    }
}
