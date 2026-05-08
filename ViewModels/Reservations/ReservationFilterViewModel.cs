using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using WebApplication1.Models;

namespace WebApplication1.ViewModels.Reservations
{
    public class ReservationFilterViewModel
    {
        [Display(Name = "Tanggal")]
        public DateTime? Date { get; set; }

        [Display(Name = "Status")]
        public string? Status { get; set; }

        [Display(Name = "Pencarian")]
        public string? Query { get; set; }

        public IEnumerable<Reservation> Reservations { get; set; } = Array.Empty<Reservation>();

        public IEnumerable<SelectListItem> StatusOptions { get; set; } = Array.Empty<SelectListItem>();
    }
}
