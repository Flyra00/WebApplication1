using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(SignupCode), IsUnique = true)]
    [Index(nameof(MidtransReference), IsUnique = true)]
    public class PendingMemberSignup
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(40)]
        public string SignupCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string PasswordHashTemp { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = PendingMemberSignupStatuses.PendingPayment;

        [Precision(16, 2)]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(100)]
        public string MidtransReference { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAtUtc { get; set; }

        public DateTime? ActivatedAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        public DateTime? FailedAtUtc { get; set; }
    }
}
