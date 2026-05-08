using System.ComponentModel.DataAnnotations;
using WebApplication1.Models;

namespace WebApplication1.ViewModels.Reservations
{
    public class ReservationLookupViewModel
    {
        [Required]
        [Display(Name = "Kode Akses / Kode Reservasi")]
        public string LookupKey { get; set; } = string.Empty;

        public Reservation? Reservation { get; set; }
    }
}
