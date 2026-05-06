using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(Phone), nameof(Purpose), nameof(CreatedAt))]
    public class PhoneOtpVerification
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string CodeHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(40)]
        public string Purpose { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public bool IsUsed { get; set; }

        public int AttemptCount { get; set; }
    }
}
