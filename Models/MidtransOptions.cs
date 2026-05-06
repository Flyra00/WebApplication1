namespace WebApplication1.Models
{
    public class MidtransOptions
    {
        public const string SectionName = "Midtrans";

        public string ServerKey { get; set; } = string.Empty;

        public string ClientKey { get; set; } = string.Empty;

        public bool IsProduction { get; set; }

        public string MerchantId { get; set; } = string.Empty;
    }
}
