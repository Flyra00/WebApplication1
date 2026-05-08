using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Midtrans;
using WebApplication1.Services.Time;
using WebApplication1.ViewModels.Reservations;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Supervisor},{AppRoles.Kasir}")]
    public class ReservationController : Controller
    {
        private static readonly TimeSpan ReservationOpenTime = TimeSpan.FromHours(10);
        private static readonly TimeSpan ReservationCloseTime = TimeSpan.FromHours(21);
        private static readonly TimeSpan ReservationSlotInterval = TimeSpan.FromMinutes(30);
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ReservationController> _logger;
        private readonly MidtransService _midtrans;
        private readonly IBusinessTime _businessTime;

        public ReservationController(
            AppDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<ReservationController> logger,
            MidtransService midtrans,
            IBusinessTime businessTime)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
            _midtrans = midtrans;
            _businessTime = businessTime;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Create()
        {
            var localDefault = GetDefaultReservationLocalDateTime();
            var model = new ReservationCreateViewModel
            {
                ReservationDate = localDefault.Date,
                StartTime = localDefault.ToString(@"hh\:mm"),
                PartySize = 1
            };

            model.StartTimeOptions = BuildReservationStartTimeOptions(model.StartTime);
            model.DurationHourOptions = BuildReservationDurationHourOptions(model.ReservationDurationHours);
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReservationCreateViewModel model)
        {
            if (ModelState.IsValid)
            {
                if (!TryParseAllowedStartTime(model.StartTime, out var reservationClock))
                {
                    ModelState.AddModelError(nameof(model.StartTime), "Silakan pilih jam mulai reservasi yang tersedia.");
                }

                if (model.ReservationDurationHours is < 1 or > 3)
                {
                    ModelState.AddModelError(nameof(model.ReservationDurationHours), "Durasi reservasi harus 1, 2, atau 3 jam.");
                }

                if (model.PartySize < 1)
                {
                    ModelState.AddModelError(nameof(model.PartySize), "Jumlah tamu harus minimal 1 orang.");
                }

                var hasActiveTable = await _context.Tables.AnyAsync(table => table.IsActive);
                if (!hasActiveTable)
                    ModelState.AddModelError(nameof(model.PartySize), "Belum ada meja aktif yang bisa dipakai untuk reservasi.");

                if (ModelState.IsValid)
                {
                    var reservationLocalTime = model.ReservationDate.Date.Add(reservationClock);
                    var reservationEndLocalTime = reservationLocalTime.AddHours(model.ReservationDurationHours);
                    var reservationTimeUtc = NormalizeFromLocal(reservationLocalTime);
                    var reservationEndUtc = NormalizeFromLocal(reservationEndLocalTime);

                    if (reservationTimeUtc <= DateTime.UtcNow)
                        ModelState.AddModelError(nameof(model.StartTime), "Waktu mulai reservasi tidak boleh di masa lalu.");

                    if (reservationEndLocalTime.TimeOfDay > ReservationCloseTime)
                        ModelState.AddModelError(nameof(model.ReservationDurationHours), "Durasi reservasi melewati jam operasional.");

                    if (ModelState.IsValid)
                    {
                        var transaction = _context.Database.IsRelational()
                            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                            : null;

                        try
                        {
                            var currentUser = User?.Identity?.IsAuthenticated == true
                                ? await _userManager.GetUserAsync(User)
                                : null;

                            var now = DateTime.UtcNow;
                            var reservation = new Reservation
                            {
                                ReservationCode = await GenerateUniqueReservationCodeAsync(),
                                AccessKey = await GenerateUniqueAccessKeyAsync(),
                                CustomerName = model.CustomerName.Trim(),
                                PhoneNumber = model.PhoneNumber.Trim(),
                                Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
                                ReservationTime = reservationTimeUtc,
                                ReservationDurationHours = model.ReservationDurationHours,
                                PartySize = model.PartySize,
                                DpPercentage = await GetAppSettingPercentageAsync(AppSettingKeys.ReservationDpPercentage),
                                SpecialRequest = string.IsNullOrWhiteSpace(model.SpecialRequest) ? null : model.SpecialRequest.Trim(),
                                Status = ReservationStatuses.Pending,
                                Source = ReservationSources.Online,
                                TableId = null,
                                CustomerUserId = currentUser?.Id,
                                CreatedAtUtc = now
                            };

                            _context.Reservations.Add(reservation);
                            await _context.SaveChangesAsync();
                            if (transaction != null)
                                await transaction.CommitAsync();

                            if (TempData != null)
                            {
                                TempData["Success"] = $"Reservasi berhasil dibuat. Kode akses Anda: {reservation.AccessKey}.";
                            }
                            return RedirectToAction(nameof(Success), new { code = reservation.ReservationCode });
                        }
                        catch
                        {
                            if (transaction != null)
                                await transaction.RollbackAsync();
                            throw;
                        }
                    }
                }
            }

            model.StartTimeOptions = BuildReservationStartTimeOptions(model.StartTime);
            model.DurationHourOptions = BuildReservationDurationHourOptions(model.ReservationDurationHours);
            return View(model);
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Success(string code)
        {
            var reservation = await GetReservationByCodeAsync(code);
            if (!IsPublicOnlineReservation(reservation))
                return NotFound();

            return View(reservation);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Lookup()
        {
            return View(new ReservationLookupViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Lookup(ReservationLookupViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var reservation = await GetReservationByCodeAsync(model.LookupKey);
            if (!IsPublicOnlineReservation(reservation))
            {
                ModelState.AddModelError(nameof(model.LookupKey), "Kode akses atau kode reservasi tidak ditemukan.");
                return View(model);
            }

            return RedirectToAction(nameof(DetailsByCode), new { code = reservation.ReservationCode });
        }

        [AllowAnonymous]
        [HttpGet("/Reservation/DetailsByCode/{code}")]
        public async Task<IActionResult> DetailsByCode(string code)
        {
            var reservation = await GetReservationByCodeAsync(code);
            if (!IsPublicOnlineReservation(reservation))
                return NotFound();

            return View(reservation);
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Menu(string code)
        {
            var reservation = await _context.Reservations
                .AsNoTracking()
                .Include(r => r.Table)
                .Include(r => r.Order)
                .ThenInclude(o => o!.Items)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(r => r.ReservationCode == (code ?? string.Empty).Trim());

            if (!IsPublicOnlineReservation(reservation))
                return NotFound();

            if (reservation.Order != null)
                return RedirectToAction(nameof(DetailsByCode), new { code = reservation.ReservationCode });

            var products = await _context.Products
                .AsNoTracking()
                .Where(product => product.IsAvailable && product.Stock > 0)
                .OrderBy(product => product.Category)
                .ThenBy(product => product.Name)
                .ToListAsync();

            return View(new ReservationMenuViewModel
            {
                Reservation = reservation,
                Products = products
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitMenu([FromBody] ReservationMenuSubmitRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
                return Json(new { success = false, error = "Data menu tidak valid." });

            var normalizedCode = request.Code.Trim();
            var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            try
            {
                var reservation = await _context.Reservations
                    .Include(r => r.Order)
                        .ThenInclude(o => o!.Items)
                    .FirstOrDefaultAsync(r => r.ReservationCode == normalizedCode);

                if (!IsPublicOnlineReservation(reservation))
                    return Json(new { success = false, error = "Reservasi tidak ditemukan." });

                if (reservation.Order != null)
                {
                    if (transaction != null)
                        await transaction.CommitAsync();
                    return Json(new
                    {
                        success = true,
                        alreadyCreated = true,
                        orderNumber = reservation.Order.OrderNumber,
                        redirectUrl = Url.Action(nameof(DetailsByCode), new { code = reservation.ReservationCode })
                    });
                }

                var items = request.Items
                    .Where(item => item.ProductId > 0 && item.Qty > 0)
                    .GroupBy(item => item.ProductId)
                    .Select(group => new ReservationMenuItemRequest
                    {
                        ProductId = group.Key,
                        Qty = group.Sum(item => item.Qty)
                    })
                    .ToList();

                if (items.Count == 0)
                    return Json(new { success = false, error = "Pesanan masih kosong." });

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

                var order = new Order
                {
                    ReservationId = reservation.Id,
                    TableSessionId = null,
                    OrderNumber = await GenerateUniqueOrderNumberAsync(),
                    CustomerUserId = reservation.CustomerUserId,
                    GuestName = reservation.CustomerName,
                    GuestPhone = reservation.PhoneNumber,
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
                if (transaction != null)
                    await transaction.CommitAsync();

                TempData["Success"] = "Menu reservasi berhasil dikonfirmasi. Silakan lanjut ke pembayaran.";
                return Json(new
                {
                    success = true,
                    orderNumber = order.OrderNumber,
                    redirectUrl = Url.Action(nameof(Pay), new { code = reservation.ReservationCode })
                });
            }
            catch (Exception ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to submit reservation menu for code {Code}", normalizedCode);
                return Json(new { success = false, error = "Menu reservasi gagal diproses. Silakan coba lagi." });
            }
        }

        // ─── Halaman pilih cara bayar ────────────────────────────────────────────
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Pay(string code)
        {
            var reservation = await GetReservationByCodeAsync(code);

            if (!IsPublicOnlineReservation(reservation)) return NotFound();

            if (reservation.Order == null)
            {
                TempData["Error"] = "Silakan pilih menu terlebih dahulu.";
                return RedirectToAction(nameof(Menu), new { code });
            }

            var order = reservation.Order;
            var paidTotal = ReservationBillingHelper.GetPaidTotal(order);
            var outstandingAmount = ReservationBillingHelper.GetOutstandingAmount(order);
            if (outstandingAmount <= 0m)
            {
                TempData["Success"] = "Reservasi ini sudah lunas.";
                return RedirectToAction(nameof(DetailsByCode), new { code });
            }

            ViewBag.IsMidtransConfigured   = _midtrans.IsConfigured;
            ViewBag.MidtransClientKey      = _midtrans.ClientKey;
            ViewBag.MidtransIsProduction   = _midtrans.IsProduction;

            ViewBag.OrderTotal = order.Total;
            ViewBag.PaidTotal = paidTotal;
            ViewBag.OutstandingAmount = outstandingAmount;
            ViewBag.DepositAmount = ReservationBillingHelper.GetDepositAmount(order, reservation);
            ViewBag.HasDepositOnly = ReservationBillingHelper.HasDepositOnly(order);

            return View(reservation);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncPaymentStatus([FromBody] ReservationPaymentSyncRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Code))
                return Json(new { success = false, error = "Data tidak valid." });

            var normalizedCode = req.Code.Trim();
            var normalizedOrderId = string.IsNullOrWhiteSpace(req.OrderId) ? null : req.OrderId.Trim();

            var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            try
            {
                Reservation? reservation = null;

                if (!string.IsNullOrWhiteSpace(normalizedCode))
                {
                    reservation = await _context.Reservations
                        .Include(r => r.Order)
                            .ThenInclude(o => o!.Payments)
                        .FirstOrDefaultAsync(r => r.ReservationCode == normalizedCode);
                }

                if (reservation == null && !string.IsNullOrWhiteSpace(normalizedOrderId))
                {
                    var payment = await _context.Payments
                        .Include(p => p.Order)
                        .FirstOrDefaultAsync(p => p.ReferenceNumber == normalizedOrderId && p.Method == PaymentMethods.Midtrans);

                    if (payment?.Order != null)
                    {
                        reservation = await _context.Reservations
                            .Include(r => r.Order)
                                .ThenInclude(o => o!.Payments)
                            .FirstOrDefaultAsync(r => r.Order != null && r.Order.Id == payment.Order.Id);
                    }
                }

                if (!IsPublicOnlineReservation(reservation))
                    return Json(new { success = false, error = "Reservasi tidak ditemukan." });

                if (reservation.Order == null)
                    return Json(new { success = false, error = "Reservasi belum memiliki order." });

                var paymentsToSync = !string.IsNullOrWhiteSpace(normalizedOrderId)
                    ? await _context.Payments
                        .Where(payment => payment.OrderId == reservation.Order.Id &&
                                          payment.Method == PaymentMethods.Midtrans &&
                                          payment.ReferenceNumber == normalizedOrderId)
                        .ToListAsync()
                    : await _context.Payments
                        .Where(payment => payment.OrderId == reservation.Order.Id &&
                                          payment.Method == PaymentMethods.Midtrans &&
                                          !string.IsNullOrWhiteSpace(payment.ReferenceNumber) &&
                                          !string.Equals(payment.Status, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(payment => payment.PaymentDate)
                        .ToListAsync();

                if (paymentsToSync.Count == 0)
                    return Json(new { success = true, synced = false, message = "Tidak ada pembayaran Midtrans yang perlu disinkronkan." });

                var syncedCount = 0;
                foreach (var payment in paymentsToSync)
                {
                    var referenceNumber = payment.ReferenceNumber?.Trim();
                    if (string.IsNullOrWhiteSpace(referenceNumber))
                        continue;

                    var statusDocument = await _midtrans.GetTransactionStatusAsync(referenceNumber);
                    if (statusDocument == null)
                        continue;

                    using var statusJson = statusDocument;
                    var root = statusJson.RootElement;
                    var mappedStatus = MapMidtransStatus(
                        root.TryGetProperty("transaction_status", out var transactionStatusElement) ? transactionStatusElement.GetString() : null,
                        root.TryGetProperty("fraud_status", out var fraudStatusElement) ? fraudStatusElement.GetString() : null);

                    if (ApplyMidtransPaymentStatus(payment, mappedStatus))
                        syncedCount++;
                }

                RefreshReservationOrderStatus(reservation.Order);

                await _context.SaveChangesAsync();

                if (transaction != null)
                    await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    synced = syncedCount > 0,
                    paymentStatus = reservation.Order.Payments
                        .Where(payment => payment.Method == PaymentMethods.Midtrans)
                        .OrderByDescending(payment => payment.PaymentDate)
                        .Select(payment => payment.Status)
                        .FirstOrDefault(),
                    orderStatus = reservation.Order.Status,
                    outstandingAmount = ReservationBillingHelper.GetOutstandingAmount(reservation.Order)
                });
            }
            catch (Exception ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();

                _logger.LogError(ex, "Failed to sync Midtrans payment status for reservation {Code}", normalizedCode);
                return Json(new { success = false, error = "Sinkronisasi pembayaran gagal." });
            }
        }

        // ─── AJAX: buat snap token Midtrans untuk reservasi ──────────────────────
        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestPaymentToken([FromBody] ReservationPayRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Code))
                return Json(new { success = false, error = "Data tidak valid." });

            var reservation = await _context.Reservations
                .Include(r => r.Order)
                    .ThenInclude(o => o!.Payments)
                .FirstOrDefaultAsync(r => r.ReservationCode == req.Code.Trim());

            if (!IsPublicOnlineReservation(reservation))
                return Json(new { success = false, error = "Reservasi tidak ditemukan." });

            if (reservation.Order == null)
                return Json(new { success = false, error = "Silakan pilih menu terlebih dahulu." });

            if (!_midtrans.IsConfigured)
                return Json(new { success = false, error = "Pembayaran digital belum tersedia saat ini." });

            var order = reservation.Order;
            var paidTotal = ReservationBillingHelper.GetPaidTotal(order);
            var outstanding = ReservationBillingHelper.GetOutstandingAmount(order);

            if (outstanding <= 0m)
                return Json(new { success = false, error = "Reservasi ini sudah lunas." });

            var selectedPayOption = (req.PayOption ?? string.Empty).Trim().ToLowerInvariant();
            var isDp = selectedPayOption == "dp-online" && paidTotal <= 0m && reservation.DpPercentage.HasValue && reservation.DpPercentage > 0;
            var isFull = selectedPayOption == "full-online" && paidTotal <= 0m;

            if (!isDp && !isFull)
                return Json(new { success = false, error = "Metode pembayaran online tidak tersedia untuk kondisi reservasi ini." });

            decimal payAmount;
            string  orderId;

            if (isDp)
            {
                payAmount = ReservationBillingHelper.GetDepositAmount(order, reservation);
                orderId   = $"RSV-DP-{reservation.ReservationCode}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            }
            else
            {
                payAmount = outstanding;
                orderId   = $"RSV-FULL-{reservation.ReservationCode}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            }

            if (payAmount < 1000) payAmount = 1000; // Midtrans minimum

            var purpose = isDp ? PaymentPurpose.ReservationDeposit : PaymentPurpose.ReservationFull;
            var existingPayment = reservation.Order.Payments.FirstOrDefault(payment =>
                payment.Method == PaymentMethods.Midtrans &&
                payment.Status == PaymentStatuses.Pending &&
                payment.Purpose == purpose &&
                payment.Amount == payAmount);

            if (existingPayment != null && !string.IsNullOrWhiteSpace(existingPayment.ReferenceNumber))
                orderId = existingPayment.ReferenceNumber;

            if (existingPayment == null)
            {
                existingPayment = new Payment
                {
                    OrderId = reservation.Order.Id,
                    Method = PaymentMethods.Midtrans,
                    Purpose = purpose,
                    Amount = payAmount,
                    PaymentDate = DateTime.UtcNow,
                    Status = PaymentStatuses.Pending,
                    ReferenceNumber = orderId,
                    PaidByUserId = null
                };

                _context.Payments.Add(existingPayment);
                await _context.SaveChangesAsync();
            }

            var snapResult = await _midtrans.CreateSnapTransactionAsync(
                orderId,
                payAmount,
                reservation.CustomerName,
                reservation.Email,
                reservation.PhoneNumber);

            if (!snapResult.Success || string.IsNullOrWhiteSpace(snapResult.Token))
            {
                _logger.LogWarning("Midtrans snap failed for reservation {Code}: {Error}",
                    reservation.ReservationCode, snapResult.ErrorMessage);
                return Json(new { success = false, error = "Gagal membuat sesi pembayaran. Coba lagi atau pilih bayar di tempat." });
            }

            _logger.LogInformation("Midtrans snap token created for reservation {Code} option={Option} amount={Amount}",
                reservation.ReservationCode, req.PayOption, payAmount);

            return Json(new { success = true, snapToken = snapResult.Token });
        }

        // ─── Pilih bayar offline ─────────────────────────────────────────────────
        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayOffline(string code)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.ReservationCode == (code ?? string.Empty).Trim());

            if (!IsPublicOnlineReservation(reservation)) return NotFound();

            if (reservation.Order == null)
            {
                TempData["Error"] = "Silakan pilih menu terlebih dahulu.";
                return RedirectToAction(nameof(Menu), new { code });
            }

            // Tidak ubah status, cukup redirect ke detail dengan pesan info
            TempData["Success"] = "Silakan bayar di kasir restoran saat Anda tiba. Tunjukkan kode reservasi Anda.";
            return RedirectToAction(nameof(DetailsByCode), new { code });
        }

        // ─── Helper request body ─────────────────────────────────────────────────
        public sealed class ReservationPayRequest
        {
            public string? Code      { get; set; }
            public string? PayOption { get; set; }
        }

        public sealed class ReservationMenuSubmitRequest
        {
            public string? Code { get; set; }
            public List<ReservationMenuItemRequest> Items { get; set; } = new();
        }

        public sealed class ReservationMenuItemRequest
        {
            public int ProductId { get; set; }
            public int Qty { get; set; }
        }

        public sealed class ReservationPaymentSyncRequest
        {
            public string? Code { get; set; }

            public string? OrderId { get; set; }
        }

        public async Task<IActionResult> Index(DateTime? date, string? status, string? query)
        {
            var filterDate = date?.Date;
            var reservationQuery = _context.Reservations
                .AsNoTracking()
                .Include(r => r.Table)
                .Include(r => r.CustomerUser)
                .Include(r => r.TableSession)
                .AsQueryable();

            if (filterDate.HasValue)
            {
                var (startUtc, endUtc) = _businessTime.GetUtcDayRange(filterDate.Value);
                reservationQuery = reservationQuery.Where(r =>
                    r.ReservationTime >= startUtc &&
                    r.ReservationTime < endUtc);
            }

            if (!string.IsNullOrWhiteSpace(status))
                reservationQuery = reservationQuery.Where(r => r.Status == status);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var term = query.Trim();
                reservationQuery = reservationQuery.Where(r =>
                    r.CustomerName.Contains(term) ||
                    r.PhoneNumber.Contains(term));
            }

            var reservations = await reservationQuery
                .OrderByDescending(r => r.ReservationTime)
                .ToListAsync();

            var model = new ReservationFilterViewModel
            {
                Date = filterDate,
                Status = status,
                Query = query,
                Reservations = reservations,
                StatusOptions = GetStatusOptions()
            };

            return View(model);
        }

        public async Task<IActionResult> Details(int id)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Table)
                .Include(r => r.CustomerUser)
                .Include(r => r.TableSession)
                    .ThenInclude(s => s!.Table)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                return NotFound();

            return View(reservation);
        }

        [HttpGet]
        public async Task<IActionResult> Confirm(int id)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Table)
                .Include(r => r.CustomerUser)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Hanya reservasi berstatus Pending yang bisa dikonfirmasi.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var model = new ReservationConfirmViewModel
            {
                ReservationId = reservation.Id,
                SelectedTableId = reservation.TableId,
                Reservation = reservation,
                TableOptions = await BuildTableOptionsAsync(reservation.PartySize)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(ReservationConfirmViewModel model)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Table)
                .FirstOrDefaultAsync(r => r.Id == model.ReservationId);

            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Reservasi hanya bisa dikonfirmasi dari status Pending.";
                return RedirectToAction(nameof(Details), new { id = reservation.Id });
            }

            var targetTable = model.SelectedTableId.HasValue
                ? await _context.Tables.FirstOrDefaultAsync(t => t.Id == model.SelectedTableId.Value && t.IsActive)
                : await FindAvailableTableAsync(reservation.PartySize, reservation.ReservationTime, reservation.ReservationDurationHours, reservation.Id);

            if (targetTable == null)
            {
                ModelState.AddModelError(nameof(model.SelectedTableId), "Tidak ada meja yang tersedia untuk reservasi ini.");
                model.Reservation = reservation;
                model.TableOptions = await BuildTableOptionsAsync(reservation.PartySize);
                return View(model);
            }

            if (targetTable.Capacity < reservation.PartySize)
            {
                ModelState.AddModelError(nameof(model.SelectedTableId), "Kapasitas meja tidak mencukupi.");
                model.Reservation = reservation;
                model.TableOptions = await BuildTableOptionsAsync(reservation.PartySize);
                return View(model);
            }

            var reservationEndUtc = reservation.ReservationTime.AddHours(reservation.ReservationDurationHours);
            var hasConflict = await HasReservationConflictAsync(targetTable.Id, reservation.ReservationTime, reservationEndUtc, reservation.Id);
            if (hasConflict)
            {
                ModelState.AddModelError(nameof(model.SelectedTableId), "Meja tersebut bentrok dengan reservasi lain.");
                model.Reservation = reservation;
                model.TableOptions = await BuildTableOptionsAsync(reservation.PartySize);
                return View(model);
            }

            var now = DateTime.UtcNow;
            reservation.TableId = targetTable.Id;
            reservation.Status = ReservationStatuses.Confirmed;
            reservation.ConfirmedAtUtc = now;
            reservation.UpdatedAtUtc = now;

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Reservasi {reservation.ReservationCode} berhasil dikonfirmasi.";
            return RedirectToAction(nameof(Details), new { id = reservation.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == id);
            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Hanya reservasi Pending yang bisa ditolak.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var now = DateTime.UtcNow;
            reservation.Status = ReservationStatuses.Rejected;
            reservation.RejectedAtUtc = now;
            reservation.UpdatedAtUtc = now;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Reservasi {reservation.ReservationCode} ditolak.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == id);
            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.Pending, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reservation.Status, ReservationStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Reservasi ini tidak bisa dibatalkan.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var now = DateTime.UtcNow;
            reservation.Status = ReservationStatuses.Cancelled;
            reservation.CancelledAtUtc = now;
            reservation.UpdatedAtUtc = now;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Reservasi {reservation.ReservationCode} dibatalkan.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(int id)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == id);
            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Hanya reservasi Confirmed yang bisa check-in.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!reservation.TableId.HasValue)
            {
                TempData["Error"] = "Reservasi harus memiliki meja sebelum check-in.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var hasOpenSession = await _context.TableSessions.AnyAsync(s =>
                s.TableId == reservation.TableId.Value &&
                s.Status == TableSessionStatuses.Open &&
                s.EndTime == null);

            if (hasOpenSession)
            {
                TempData["Error"] = "Meja sedang dipakai dan tidak bisa check-in.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var now = DateTime.UtcNow;
            var session = new TableSession
            {
                TableId = reservation.TableId.Value,
                SessionCode = await GenerateUniqueSessionCodeAsync(),
                GuestType = reservation.CustomerUserId != null ? TableGuestTypes.Member : TableGuestTypes.Guest,
                MemberUserId = reservation.CustomerUserId,
                StartTime = now,
                Status = TableSessionStatuses.Open
            };

            reservation.TableSession = session;
            reservation.Status = ReservationStatuses.CheckedIn;
            reservation.CheckedInAtUtc = now;
            reservation.UpdatedAtUtc = now;

            _context.TableSessions.Add(session);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Reservasi {reservation.ReservationCode} berhasil check-in.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkNoShow(int id)
        {
            var reservation = await _context.Reservations.FirstOrDefaultAsync(r => r.Id == id);
            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Hanya reservasi Confirmed yang bisa ditandai No Show.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var now = DateTime.UtcNow;
            reservation.Status = ReservationStatuses.NoShow;
            reservation.NoShowAtUtc = now;
            reservation.UpdatedAtUtc = now;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Reservasi {reservation.ReservationCode} ditandai No Show.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id)
        {
            var reservation = await _context.Reservations
                .Include(r => r.TableSession)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (reservation == null)
                return NotFound();

            if (!string.Equals(reservation.Status, ReservationStatuses.CheckedIn, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Hanya reservasi CheckedIn yang bisa diselesaikan.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var now = DateTime.UtcNow;
            reservation.Status = ReservationStatuses.Completed;
            reservation.CompletedAtUtc = now;
            reservation.UpdatedAtUtc = now;

            if (reservation.TableSession != null &&
                string.Equals(reservation.TableSession.Status, TableSessionStatuses.Open, StringComparison.OrdinalIgnoreCase) &&
                reservation.TableSession.EndTime == null)
            {
                reservation.TableSession.Status = TableSessionStatuses.Closed;
                reservation.TableSession.EndTime = now;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Reservasi {reservation.ReservationCode} selesai.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<Reservation?> GetReservationByCodeAsync(string code)
        {
            var normalizedCode = (code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedCode))
                return null;

            var upperCode = normalizedCode.ToUpperInvariant();

            return await _context.Reservations
                .AsNoTracking()
                .Include(r => r.Table)
                .Include(r => r.CustomerUser)
                .Include(r => r.Order)
                    .ThenInclude(o => o!.Items)
                        .ThenInclude(i => i.Product)
                .Include(r => r.Order)
                    .ThenInclude(o => o!.Payments)
                .Include(r => r.TableSession)
                    .ThenInclude(s => s!.Table)
                .FirstOrDefaultAsync(r => r.ReservationCode == normalizedCode || r.ReservationCode == upperCode || r.AccessKey == normalizedCode || r.AccessKey == upperCode);
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

        private static void RefreshReservationOrderStatus(Order order)
        {
            if (string.Equals(order.Status, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.Status, OrderStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var paidTotal = ReservationBillingHelper.GetPaidTotal(order);
            order.Status = paidTotal >= order.Total ? OrderStatuses.Paid : OrderStatuses.Submitted;
        }

        private static bool IsPublicOnlineReservation(Reservation? reservation)
        {
            return reservation != null &&
                   (string.IsNullOrWhiteSpace(reservation.Source) ||
                    string.Equals(reservation.Source, ReservationSources.Online, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<IEnumerable<SelectListItem>> BuildTableOptionsAsync(int partySize)
        {
            var tables = await _context.Tables
                .AsNoTracking()
                .Where(t => t.IsActive && t.Capacity >= partySize)
                .OrderBy(t => t.Capacity)
                .ThenBy(t => t.Number)
                .ToListAsync();

            return tables.Select(t => new SelectListItem
            {
                Value = t.Id.ToString(),
                Text = $"Meja {t.Number} - Kapasitas {t.Capacity}"
            });
        }

        private IEnumerable<SelectListItem> BuildCreateTableOptions(int? selectedTableId = null)
        {
            return _context.Tables
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Number)
                .Select(t => new SelectListItem
                {
                    Value = t.Id.ToString(),
                    Text = $"Meja {t.Number} - Kapasitas {t.Capacity}",
                    Selected = selectedTableId.HasValue && t.Id == selectedTableId.Value
                })
                .ToList();
        }

        private static IEnumerable<SelectListItem> GetStatusOptions()
        {
            return new[]
            {
                new SelectListItem { Text = "Semua", Value = string.Empty },
                new SelectListItem { Text = ReservationStatuses.Pending, Value = ReservationStatuses.Pending },
                new SelectListItem { Text = ReservationStatuses.Confirmed, Value = ReservationStatuses.Confirmed },
                new SelectListItem { Text = ReservationStatuses.Rejected, Value = ReservationStatuses.Rejected },
                new SelectListItem { Text = ReservationStatuses.Cancelled, Value = ReservationStatuses.Cancelled },
                new SelectListItem { Text = ReservationStatuses.CheckedIn, Value = ReservationStatuses.CheckedIn },
                new SelectListItem { Text = ReservationStatuses.Completed, Value = ReservationStatuses.Completed },
                new SelectListItem { Text = ReservationStatuses.NoShow, Value = ReservationStatuses.NoShow }
            };
        }

        private async Task<decimal> GetAppSettingPercentageAsync(string key)
        {
            var rawValue = await _context.AppSettings
                .AsNoTracking()
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(rawValue))
                return 0m;

            return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }

        private async Task<bool> HasReservationConflictAsync(int tableId, DateTime reservationStartUtc, DateTime reservationEndUtc, int? excludeReservationId = null)
        {
            return await _context.Reservations.AnyAsync(r =>
                r.TableId == tableId &&
                (!excludeReservationId.HasValue || r.Id != excludeReservationId.Value) &&
                (r.Status == ReservationStatuses.Pending || r.Status == ReservationStatuses.Confirmed || r.Status == ReservationStatuses.CheckedIn) &&
                r.ReservationTime < reservationEndUtc &&
                r.ReservationTime.AddHours(r.ReservationDurationHours) > reservationStartUtc);
        }

        private static IEnumerable<SelectListItem> BuildReservationStartTimeOptions(string? selectedValue = null)
        {
            return GetAllowedReservationTimeSlots().Select(slot => new SelectListItem
            {
                Value = slot,
                Text = slot,
                Selected = string.Equals(slot, selectedValue, StringComparison.OrdinalIgnoreCase)
            });
        }

        private static IEnumerable<SelectListItem> BuildReservationDurationHourOptions(int selectedValue = 2)
        {
            return new[]
            {
                new SelectListItem { Value = "1", Text = "1 jam", Selected = selectedValue == 1 },
                new SelectListItem { Value = "2", Text = "2 jam", Selected = selectedValue == 2 },
                new SelectListItem { Value = "3", Text = "3 jam", Selected = selectedValue == 3 }
            };
        }

        private async Task<Table?> FindAvailableTableAsync(int partySize, DateTime reservationTimeUtc, int durationHours, int? excludeReservationId = null)
        {
            var tables = await _context.Tables
                .AsNoTracking()
                .Where(t => t.IsActive && t.Capacity >= partySize)
                .OrderBy(t => t.Capacity)
                .ThenBy(t => t.Number)
                .ToListAsync();

            foreach (var table in tables)
            {
                var hasConflict = await HasReservationConflictAsync(table.Id, reservationTimeUtc, reservationTimeUtc.AddHours(durationHours), excludeReservationId);
                if (!hasConflict)
                    return table;
            }

            return null;
        }

        private async Task<string> GenerateUniqueReservationCodeAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var code = $"RSV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                var exists = await _context.Reservations.AnyAsync(r => r.ReservationCode == code);
                if (!exists)
                    return code;
            }

            throw new InvalidOperationException("Gagal membuat kode reservasi yang unik.");
        }

        private async Task<string> GenerateUniqueAccessKeyAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var key = $"AK-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6))}";
                var exists = await _context.Reservations.AnyAsync(r => r.AccessKey == key);
                if (!exists)
                    return key;
            }

            throw new InvalidOperationException("Gagal membuat kode akses yang unik.");
        }

        private async Task<string> GenerateUniqueOrderNumberAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var code = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                var exists = await _context.Orders.AnyAsync(order => order.OrderNumber == code);
                if (!exists)
                    return code;
            }

            throw new InvalidOperationException("Gagal membuat nomor order yang unik.");
        }

        private async Task<string> GenerateUniqueSessionCodeAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var code = $"SES-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
                var exists = await _context.TableSessions.AnyAsync(s => s.SessionCode == code);
                if (!exists)
                    return code;
            }

            throw new InvalidOperationException("Gagal membuat kode sesi meja yang unik.");
        }

        private static IReadOnlyList<string> GetAllowedReservationTimeSlots()
        {
            var slots = new List<string>();
            for (var current = ReservationOpenTime; current <= ReservationCloseTime; current += ReservationSlotInterval)
            {
                slots.Add(current.ToString(@"hh\:mm"));
            }

            return slots;
        }

        private static bool TryParseAllowedStartTime(string? value, out TimeSpan slot)
        {
            slot = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (!TimeSpan.TryParseExact(value.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out slot))
                return false;

            if (slot < ReservationOpenTime || slot > ReservationCloseTime)
                return false;

            var minutesFromOpen = (slot - ReservationOpenTime).TotalMinutes;
            return Math.Abs(minutesFromOpen % ReservationSlotInterval.TotalMinutes) < double.Epsilon;
        }

        private DateTime GetDefaultReservationLocalDateTime()
        {
            var now = _businessTime.BusinessNow;
            var roundedMinutes = Math.Ceiling(now.Minute / 30d) * 30;
            var candidate = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddMinutes(roundedMinutes);
            var defaultDuration = TimeSpan.FromHours(2);

            if (candidate.TimeOfDay < ReservationOpenTime)
                candidate = candidate.Date.Add(ReservationOpenTime);

            if (candidate.TimeOfDay > ReservationCloseTime || candidate.TimeOfDay.Add(defaultDuration) > ReservationCloseTime)
                candidate = now.Date.AddDays(1).Add(ReservationOpenTime);

            return candidate;
        }

        private DateTime NormalizeFromLocal(DateTime value)
        {
            return _businessTime.ToUtc(value);
        }

        private static decimal NormalizePercentage(decimal value)
        {
            return value < 0m ? 0m : value;
        }
    }
}
