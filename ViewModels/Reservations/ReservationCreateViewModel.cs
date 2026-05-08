using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebApplication1.ViewModels.Reservations
{
    public class ReservationCreateViewModel
    {
        private int _reservationDurationHours = 2;

        [Required]
        [StringLength(100)]
        [Display(Name = "Nama Customer")]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "Nomor HP")]
        public string PhoneNumber { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(100)]
        [Display(Name = "Email (Opsional)")]
        public string? Email { get; set; }

        [Required]
        [Display(Name = "Tanggal Reservasi")]
        [DataType(DataType.Date)]
        public DateTime ReservationDate { get; set; } = DateTime.Today;

        [Required]
        [Display(Name = "Jam Mulai")]
        public string StartTime { get; set; } = "19:00";

        [Required]
        [Range(1, 100)]
        [Display(Name = "Jumlah Tamu")]
        public int PartySize { get; set; } = 1;

        [Required]
        [Range(1, 3)]
        [Display(Name = "Durasi (Jam)")]
        public int ReservationDurationHours
        {
            get => _reservationDurationHours;
            set => _reservationDurationHours = value;
        }

        public int DurationHours
        {
            get => ReservationDurationHours;
            set => ReservationDurationHours = value;
        }

        public IEnumerable<SelectListItem> StartTimeOptions { get; set; } = Array.Empty<SelectListItem>();

        public IEnumerable<SelectListItem> DurationHourOptions { get; set; } = Array.Empty<SelectListItem>();

        [StringLength(500)]
        [Display(Name = "Permintaan Khusus")]
        public string? SpecialRequest { get; set; }
    }
}
