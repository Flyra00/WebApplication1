using WebApplication1.Models;

namespace WebApplication1.ViewModels.Reservations
{
    public class ReservationMenuViewModel
    {
        public Reservation Reservation { get; set; } = null!;

        public List<Product> Products { get; set; } = new();
    }
}
