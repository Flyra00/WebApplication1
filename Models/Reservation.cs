using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(ReservationCode), IsUnique = true)]
    [Index(nameof(AccessKey), IsUnique = true)]
    [Index(nameof(Status))]
    [Index(nameof(ReservationTime))]
    [Index(nameof(TableId))]
    public class Reservation
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(40)]
        public string ReservationCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string AccessKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [EmailAddress]
        [MaxLength(100)]
        public string? Email { get; set; }

        public DateTime ReservationTime { get; set; }

        [Range(1, 3)]
        public int ReservationDurationHours { get; set; } = 2;

        [NotMapped]
        public int DurationHours
        {
            get => ReservationDurationHours;
            set => ReservationDurationHours = value;
        }

        [NotMapped]
        public DateTime ReservationDate => ReservationTime.ToLocalTime().Date;

        [NotMapped]
        public string StartTime => ReservationTime.ToLocalTime().ToString(@"HH\:mm");

        [Range(1, 100)]
        public int PartySize { get; set; }

        [Precision(5, 2)]
        public decimal? DpPercentage { get; set; }

        [MaxLength(500)]
        public string? SpecialRequest { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = ReservationStatuses.Pending;

        [Required]
        [MaxLength(20)]
        public string Source { get; set; } = ReservationSources.Online;

        public int? TableId { get; set; }

        [MaxLength(450)]
        public string? CustomerUserId { get; set; }

        public int? TableSessionId { get; set; }

        public DateTime? ConfirmedAtUtc { get; set; }

        public DateTime? CheckedInAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        public DateTime? RejectedAtUtc { get; set; }

        public DateTime? NoShowAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public Table? Table { get; set; }

        public ApplicationUser? CustomerUser { get; set; }

        public TableSession? TableSession { get; set; }

        public Order? Order { get; set; }
    }
}
