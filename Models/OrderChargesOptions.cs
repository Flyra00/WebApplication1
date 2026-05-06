namespace WebApplication1.Models
{
    public class OrderChargesOptions
    {
        public const string SectionName = "OrderCharges";

        public decimal? PpnPercentage { get; set; }

        public decimal? ServicePercentage { get; set; }
    }
}
