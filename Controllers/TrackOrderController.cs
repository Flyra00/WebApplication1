using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using WebApplication1.ViewModels.TrackOrder;

namespace WebApplication1.Controllers
{
    [AllowAnonymous]
    public class TrackOrderController : Controller
    {
        private readonly AppDbContext _context;

        public TrackOrderController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("/TrackOrder/{orderNumber}")]
        public async Task<IActionResult> Index(string orderNumber)
        {
            var order = await LoadOrderAsync(orderNumber);
            if (order == null)
                return NotFound();

            var payment = await LoadPaymentAsync(order.Id);
            return View(BuildViewModel(order, payment));
        }

        [HttpGet("/TrackOrder/{orderNumber}/Status")]
        public async Task<IActionResult> Status(string orderNumber)
        {
            var order = await LoadOrderAsync(orderNumber);
            if (order == null)
                return NotFound();

            var payment = await LoadPaymentAsync(order.Id);
            var vm = BuildViewModel(order, payment);

            return Json(new
            {
                success = true,
                orderNumber = vm.OrderNumber,
                orderStatus = vm.OrderStatus,
                orderStatusLabel = vm.OrderStatusLabel,
                progressPercent = vm.ProgressPercent,
                paymentStatusLabel = vm.PaymentStatusLabel,
                items = vm.Items.Select(item => new
                {
                    productName = item.ProductName,
                    qty = item.Qty,
                    kitchenStatus = item.KitchenStatus,
                    kitchenStatusLabel = item.KitchenStatusLabel
                })
            });
        }

        private async Task<Order?> LoadOrderAsync(string orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber) || !IsOrderNumberFormatValid(orderNumber))
                return null;

            return await _context.Orders
                .AsNoTracking()
                .Include(order => order.Items)
                    .ThenInclude(item => item.Product)
                .FirstOrDefaultAsync(order => order.OrderNumber == orderNumber);
        }

        private async Task<Payment?> LoadPaymentAsync(int orderId)
        {
            return await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.OrderId == orderId)
                .OrderByDescending(payment => payment.Id)
                .FirstOrDefaultAsync();
        }

        private static TrackOrderViewModel BuildViewModel(Order order, Payment? payment)
        {
            return new TrackOrderViewModel
            {
                OrderNumber = order.OrderNumber,
                OrderStatus = order.Status,
                OrderStatusLabel = MapOrderStatusLabel(order.Status),
                ProgressPercent = MapProgressPercent(order.Status),
                PaymentStatusLabel = MapPaymentStatusLabel(order, payment),
                OrderDate = order.OrderDate,
                Items = order.Items
                    .OrderBy(item => item.Id)
                    .Select(item => new TrackOrderItemViewModel
                    {
                        ProductName = item.Product?.Name ?? "Menu",
                        Qty = item.Qty,
                        KitchenStatus = item.KitchenStatus,
                        KitchenStatusLabel = MapKitchenStatusLabel(item.KitchenStatus)
                    })
                    .ToList()
            };
        }

        private static string MapOrderStatusLabel(string status)
        {
            return status switch
            {
                OrderStatuses.Submitted => "Pesanan Dikirim",
                OrderStatuses.Processing => "Sedang Dimasak",
                OrderStatuses.Completed => "Siap Disajikan",
                OrderStatuses.Paid => "Selesai Dibayar",
                OrderStatuses.Cancelled => "Dibatalkan",
                _ => "Menunggu Update"
            };
        }

        private static int MapProgressPercent(string status)
        {
            return status switch
            {
                OrderStatuses.Submitted => 25,
                OrderStatuses.Processing => 50,
                OrderStatuses.Completed => 75,
                OrderStatuses.Paid => 100,
                _ => 0
            };
        }

        private static string MapKitchenStatusLabel(string status)
        {
            return status switch
            {
                KitchenStatuses.Queued => "Antri",
                KitchenStatuses.Cooking => "Dimasak",
                KitchenStatuses.Ready => "Siap",
                KitchenStatuses.Served => "Disajikan",
                _ => "Menunggu"
            };
        }

        private static string MapPaymentStatusLabel(Order order, Payment? payment)
        {
            if (payment == null)
                return string.Equals(order.Status, OrderStatuses.Paid, StringComparison.OrdinalIgnoreCase)
                    ? "Lunas"
                    : "Menunggu Pembayaran";

            return payment.Status switch
            {
                PaymentStatuses.Paid => "Lunas",
                PaymentStatuses.Pending => "Menunggu Pembayaran",
                PaymentStatuses.Failed => "Gagal",
                PaymentStatuses.Refunded => "Dikembalikan",
                _ => "Menunggu Pembayaran"
            };
        }

        private static bool IsOrderNumberFormatValid(string orderNumber)
        {
            var value = orderNumber.Trim();
            return value.StartsWith("ORD-", StringComparison.OrdinalIgnoreCase) && value.Length is >= 12 and <= 40;
        }
    }
}
