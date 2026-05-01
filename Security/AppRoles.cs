namespace WebApplication1.Security
{
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Supervisor = "Supervisor";
        public const string Kasir = "Kasir";
        public const string Owner = "Owner";
        public const string Kitchen = "Kitchen";
        public const string Customer = "Customer";

        public static readonly string[] All =
        {
            Admin,
            Supervisor,
            Kasir,
            Owner,
            Kitchen,
            Customer
        };
    }
}
