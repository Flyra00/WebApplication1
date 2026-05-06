using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(OrderNumber), IsUnique = true)]
    public class Order
    {
        public int Id { get; set; }

        [Required]
        public int TableSessionId { get; set; }

        [Required]
        [MaxLength(40)]
        public string OrderNumber { get; set; } = string.Empty;

        [MaxLength(450)]
        public string? CustomerUserId { get; set; }

        [MaxLength(100)]
        public string? GuestName { get; set; }

        [MaxLength(20)]
        public string? GuestPhone { get; set; }

        [Required]
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = OrderStatuses.Submitted;

        [Required]
        [MaxLength(20)]
        public string OrderType { get; set; } = OrderTypes.DineIn;

        [Precision(16, 2)]
        public decimal Subtotal { get; set; }

        [Precision(5, 2)]
        public decimal? PpnPercentage { get; set; }

        [Precision(16, 2)]
        public decimal PpnAmount { get; set; }

        [Precision(5, 2)]
        public decimal? ServicePercentage { get; set; }

        [Precision(16, 2)]
        public decimal ServiceAmount { get; set; }

        [Precision(16, 2)]
        public decimal Total { get; set; }

        public TableSession TableSession { get; set; } = null!;

        public ApplicationUser? CustomerUser { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}
