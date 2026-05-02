using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{
    public static class DamageReportStatuses
    {
        public const string Reported  = "Reported";
        public const string Reviewed  = "Reviewed";
        public const string Resolved  = "Resolved";
    }

    public class DamageReport
    {
        public int Id { get; set; }

        [Required]
        public int InventoryItemId { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Jumlah minimal 1.")]
        public int Qty { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(450)]
        public string ReportedByUserId { get; set; } = string.Empty;

        public DateTime ReportDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = DamageReportStatuses.Reported;

        public Inventory InventoryItem { get; set; } = null!;

        public ApplicationUser ReportedByUser { get; set; } = null!;
    }
}
