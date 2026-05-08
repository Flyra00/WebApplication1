namespace WebApplication1.Configuration
{
    public sealed class PublicSiteOptions
    {
        public const string SectionName = "PublicSite";

        public string BrandName { get; set; } = "Nusantara Heritage";

        public string BrandSubtitle { get; set; } = "Cita Rasa Nusantara Berkelas";

        public string FullAddress { get; set; } = "Ganti dengan alamat restoran Anda";

        public string GoogleMapsUrl { get; set; } = string.Empty;

        public string PhoneNumber { get; set; } = "0812-0000-0000";

        public string WhatsAppNumber { get; set; } = "6281200000000";

        public string OpeningHours { get; set; } = "Setiap hari, 10.00 - 22.00";

        public string InstagramUrl { get; set; } = "https://instagram.com/nusantaraheritage";

        public string FacebookUrl { get; set; } = string.Empty;

        public string TiktokUrl { get; set; } = string.Empty;

        public string ContactEmail { get; set; } = string.Empty;
    }
}
