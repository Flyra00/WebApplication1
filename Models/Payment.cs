using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        [MaxLength(20)]
        public string Method { get; set; } = PaymentMethods.Cash;

        [Required]
        [MaxLength(40)]
        public string Purpose { get; set; } = PaymentPurpose.OrderRegular;

        [Precision(16, 2)]
        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = PaymentStatuses.Pending;

        [MaxLength(100)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(450)]
        public string? PaidByUserId { get; set; }

        public Order Order { get; set; } = null!;

        public ApplicationUser? PaidByUser { get; set; }
    }
}
