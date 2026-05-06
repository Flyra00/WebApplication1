using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json.Serialization;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.Security;
using WebApplication1.Services.Midtrans;

namespace WebApplication1.Controllers
{
    public class PaymentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MidtransService _midtransService;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(AppDbContext context, MidtransService midtransService, ILogger<PaymentController> logger)
        {
            _context = context;
            _midtransService = midtransService;
            _logger = logger;
        }

        [HttpPost("/Payment/MidtransWebhook")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> MidtransWebhook([FromBody] MidtransWebhookPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.OrderId))
            {
                _logger.LogWarning("Midtrans webhook rejected: invalid payload");
                return BadRequest(new { success = false, error = "Payload tidak valid." });
            }

            var isValidSignature = _midtransService.VerifySignature(
                payload.OrderId,
                payload.StatusCode ?? string.Empty,
                payload.GrossAmount ?? string.Empty,
                payload.SignatureKey ?? string.Empty);

            if (!isValidSignature)
            {
                _logger.LogWarning("Midtrans webhook rejected: invalid signature for order {OrderId}", payload.OrderId);
                return Unauthorized(new { success = false, error = "Signature tidak valid." });
            }

            var payment = await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.ReferenceNumber == payload.OrderId && p.Method == PaymentMethods.Midtrans);

            if (payment == null)
            {
                _logger.LogWarning("Midtrans webhook order not found: {OrderId}", payload.OrderId);
                return NotFound(new { success = false, error = "Transaksi tidak ditemukan." });
            }

            var mappedStatus = MapMidtransStatus(payload.TransactionStatus, payload.FraudStatus);
            var currentRank = GetStatusRank(payment.Status);
            var incomingRank = GetStatusRank(mappedStatus);

            if (incomingRank >= currentRank)
            {
                var previousStatus = payment.Status;
                var previousOrderStatus = payment.Order.Status;
                var shouldUpdatePaymentStatus = !string.Equals(payment.Status, mappedStatus, StringComparison.OrdinalIgnoreCase);

                if (shouldUpdatePaymentStatus)
                {
                    payment.Status = mappedStatus;
                    payment.PaymentDate = DateTime.UtcNow;
                }

                if (mappedStatus == PaymentStatuses.Paid)
                {
                    payment.Order.Status = OrderStatuses.Paid;
                }

                if (mappedStatus == PaymentStatuses.Failed && payment.Order.Status == OrderStatuses.Paid)
                {
                    payment.Order.Status = OrderStatuses.Submitted;
                }

                var shouldPersistOrderStatus = !string.Equals(previousOrderStatus, payment.Order.Status, StringComparison.OrdinalIgnoreCase);

                if (shouldUpdatePaymentStatus || shouldPersistOrderStatus)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "Midtrans webhook status processed for order {OrderNumber}: {OldStatus} -> {NewStatus}",
                        payment.Order.OrderNumber,
                        previousStatus,
                        mappedStatus);
                }
            }

            return Ok(new { success = true });
        }

        [HttpGet("/Payment/Status/{orderNumber}")]
        [AllowAnonymous]
        public async Task<IActionResult> Status(string orderNumber, [FromQuery] string? tableToken = null)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
                return BadRequest(new { success = false, error = "Order number tidak valid." });

            if (!IsOrderNumberFormatValid(orderNumber))
                return NotFound(new { success = false, error = "Status pembayaran tidak ditemukan." });

            var payment = await _context.Payments
                .Include(p => p.Order)
                .ThenInclude(o => o.TableSession)
                .ThenInclude(s => s.Table)
                .FirstOrDefaultAsync(p => p.ReferenceNumber == orderNumber && p.Method == PaymentMethods.Midtrans);

            if (payment == null)
                return NotFound(new { success = false, error = "Status pembayaran tidak ditemukan." });

            if (!CanAccessPaymentStatus(payment, tableToken))
            {
                _logger.LogWarning("Payment status access denied for order {OrderNumber}", orderNumber);
                return NotFound(new { success = false, error = "Status pembayaran tidak ditemukan." });
            }

            return Json(new
            {
                success = true,
                orderNumber,
                paymentStatus = payment.Status,
                orderStatus = payment.Order.Status,
                isPaid = payment.Status == PaymentStatuses.Paid
            });
        }

        private bool CanAccessPaymentStatus(Payment payment, string? tableToken)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole(AppRoles.Admin) ||
                    User.IsInRole(AppRoles.Kasir) ||
                    User.IsInRole(AppRoles.Owner) ||
                    User.IsInRole(AppRoles.Supervisor) ||
                    User.IsInRole(AppRoles.Kitchen))
                {
                    return true;
                }

                if (User.IsInRole(AppRoles.Customer))
                {
                    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (!string.IsNullOrWhiteSpace(currentUserId) &&
                        string.Equals(payment.Order.CustomerUserId, currentUserId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            var normalizedToken = (tableToken ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedToken))
                return false;

            var orderTableToken = payment.Order?.TableSession?.Table?.QrCodeToken ?? string.Empty;
            return string.Equals(orderTableToken, normalizedToken, StringComparison.Ordinal);
        }

        private static bool IsOrderNumberFormatValid(string orderNumber)
        {
            var value = orderNumber.Trim();
            if (!value.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase))
                return false;
            if (value.Length < 12 || value.Length > 40)
                return false;
            return true;
        }

        private static string MapMidtransStatus(string? transactionStatus, string? fraudStatus)
        {
            var status = (transactionStatus ?? string.Empty).Trim().ToLowerInvariant();
            var fraud = (fraudStatus ?? string.Empty).Trim().ToLowerInvariant();

            if (status == "capture")
            {
                return fraud == "accept" ? PaymentStatuses.Paid : PaymentStatuses.Pending;
            }

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

        public sealed class MidtransWebhookPayload
        {
            [JsonPropertyName("transaction_status")]
            public string? TransactionStatus { get; set; }

            [JsonPropertyName("fraud_status")]
            public string? FraudStatus { get; set; }

            [JsonPropertyName("order_id")]
            public string? OrderId { get; set; }

            [JsonPropertyName("status_code")]
            public string? StatusCode { get; set; }

            [JsonPropertyName("gross_amount")]
            public string? GrossAmount { get; set; }

            [JsonPropertyName("signature_key")]
            public string? SignatureKey { get; set; }

            [JsonPropertyName("transaction_id")]
            public string? TransactionId { get; set; }
        }
    }
}
