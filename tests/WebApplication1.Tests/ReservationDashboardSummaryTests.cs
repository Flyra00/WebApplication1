using WebApplication1.Models;

namespace WebApplication1.Tests;

public class ReservationDashboardSummaryTests
{
    [Fact]
    public void FromReservations_DistinguishesPaidPendingAndOutstandingReservations()
    {
        var pendingReservation = new Reservation
        {
            Status = ReservationStatuses.Pending,
            Order = null
        };

        var partiallyPaidReservation = new Reservation
        {
            Status = ReservationStatuses.Confirmed,
            Order = new Order
            {
                Total = 100_000m,
                Payments =
                [
                    new Payment
                    {
                        Status = PaymentStatuses.Paid,
                        Purpose = PaymentPurpose.ReservationDeposit,
                        Amount = 50_000m
                    }
                ]
            }
        };

        var fullyPaidReservation = new Reservation
        {
            Status = ReservationStatuses.CheckedIn,
            Order = new Order
            {
                Total = 120_000m,
                Payments =
                [
                    new Payment
                    {
                        Status = PaymentStatuses.Paid,
                        Purpose = PaymentPurpose.ReservationFull,
                        Amount = 120_000m
                    }
                ]
            }
        };

        var summary = ReservationDashboardSummary.FromReservations(
        [
            pendingReservation,
            partiallyPaidReservation,
            fullyPaidReservation
        ]);

        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(1, summary.ConfirmedCount);
        Assert.Equal(1, summary.CheckedInCount);
        Assert.Equal(2, summary.WithPaidPaymentCount);
        Assert.Equal(1, summary.PaymentPendingCount);
        Assert.Equal(1, summary.UnpaidRemainderCount);
    }
}
