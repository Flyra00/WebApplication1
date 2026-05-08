using System.Globalization;

namespace WebApplication1.Models
{
    public static class ReservationBillingHelper
    {
        public static bool HasAnyPaidPayment(Order? order)
        {
            return GetPaidTotal(order) > 0m;
        }

        public static decimal GetPaidTotal(Order? order)
        {
            if (order?.Payments == null)
                return 0m;

            return order.Payments
                .Where(payment => string.Equals(payment.Status, PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                .Sum(payment => payment.Amount);
        }

        public static decimal GetOutstandingAmount(Order? order)
        {
            if (order == null)
                return 0m;

            return Math.Max(order.Total - GetPaidTotal(order), 0m);
        }

        public static bool HasOutstandingBalance(Order? order)
        {
            return order != null && HasAnyPaidPayment(order) && GetOutstandingAmount(order) > 0m;
        }

        public static decimal GetDepositAmount(Order order, Reservation? reservation)
        {
            var percentage = reservation?.DpPercentage ?? 0m;
            if (percentage <= 0m)
                return order.Total;

            return Math.Round(order.Total * percentage / 100m, 0, MidpointRounding.AwayFromZero);
        }

        public static bool HasDepositOnly(Order? order)
        {
            return HasOutstandingBalance(order);
        }

        public static bool IsFullyPaid(Order? order)
        {
            return order != null && GetOutstandingAmount(order) <= 0m;
        }

        public static string FormatCurrency(decimal amount)
        {
            return amount.ToString("N0", CultureInfo.GetCultureInfo("id-ID"));
        }
    }
}
