using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(SessionCode), IsUnique = true)]
    public class TableSession
    {
        public int Id { get; set; }

        public int TableId { get; set; }

        [Required]
        [MaxLength(40)]
        public string SessionCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string GuestType { get; set; } = TableGuestTypes.Guest;

        [MaxLength(450)]
        public string? MemberUserId { get; set; }

        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public DateTime? EndTime { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = TableSessionStatuses.Open;

        public Table Table { get; set; } = null!;

        public ApplicationUser? MemberUser { get; set; }

        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}
