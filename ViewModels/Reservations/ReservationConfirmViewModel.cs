using Microsoft.AspNetCore.Mvc.Rendering;
using WebApplication1.Models;

namespace WebApplication1.ViewModels.Reservations
{
    public class ReservationConfirmViewModel
    {
        public int ReservationId { get; set; }

        public int? SelectedTableId { get; set; }

        public IEnumerable<SelectListItem> TableOptions { get; set; } = Array.Empty<SelectListItem>();

        public Reservation? Reservation { get; set; }
    }
}
