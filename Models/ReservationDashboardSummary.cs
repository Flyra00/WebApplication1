namespace WebApplication1.Models
{
    public sealed class ReservationDashboardSummary
    {
        public int PendingCount { get; init; }

        public int ConfirmedCount { get; init; }

        public int CheckedInCount { get; init; }

        public int WithPaidPaymentCount { get; init; }

        public int PaymentPendingCount { get; init; }

        public int UnpaidRemainderCount { get; init; }

        public static ReservationDashboardSummary FromReservations(IEnumerable<Reservation> reservations)
        {
            var reservationList = reservations?.ToList() ?? [];

            return new ReservationDashboardSummary
            {
                PendingCount = reservationList.Count(r => r.Status == ReservationStatuses.Pending),
                ConfirmedCount = reservationList.Count(r => r.Status == ReservationStatuses.Confirmed),
                CheckedInCount = reservationList.Count(r => r.Status == ReservationStatuses.CheckedIn),
                WithPaidPaymentCount = reservationList.Count(r => ReservationBillingHelper.HasAnyPaidPayment(r.Order)),
                PaymentPendingCount = reservationList.Count(r => !ReservationBillingHelper.HasAnyPaidPayment(r.Order)),
                UnpaidRemainderCount = reservationList.Count(r => ReservationBillingHelper.HasOutstandingBalance(r.Order))
            };
        }
    }
}
