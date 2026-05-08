using System.ComponentModel.DataAnnotations;

namespace WebApplication1.ViewModels.Admin
{
    public class AdminChargesSettingsViewModel
    {
        [Display(Name = "PPN Pesanan (%)")]
        [Range(0, 100)]
        public decimal OrderPpnPercentage { get; set; }

        [Display(Name = "Service Pesanan (%)")]
        [Range(0, 100)]
        public decimal OrderServicePercentage { get; set; }

        [Display(Name = "DP Reservasi (%)")]
        [Range(0, 100)]
        public decimal ReservationDpPercentage { get; set; }
    }
}
