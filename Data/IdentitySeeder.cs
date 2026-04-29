using Microsoft.AspNetCore.Identity;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            string[] roles = { "Kasir", "Owner", "Supervisor", "Customer", "Admin" };

            foreach (var r in roles)
            {
                if (!await roleManager.RoleExistsAsync(r))
                    await roleManager.CreateAsync(new IdentityRole(r));
            }

            // Admin default ini yang dirubah dan untuk menggunakan 
            // klik view > Terminal > Copy "setx ADMIN_DEFAULT_PASSWORD "admin123456*"" (password bebas kalian tentukan)
            const string adminUsername = "admin_flyra";//kasir_hadi, owner_hadi,supervisor_hadi, hadi
            var adminPassword = GetRequiredSecret(
                config,
                key: "ADMIN_DEFAULT_PASSWORD",
                errorMessage:
                    "ADMIN_DEFAULT_PASSWORD belum diset. Set via:\n" +
                    "1) dotnet user-secrets set \"ADMIN_DEFAULT_PASSWORD\" \"<password>\"\n" +
                    "atau\n" +
                    "2) Environment Variable ADMIN_DEFAULT_PASSWORD"
            );

            var admin = await userManager.FindByNameAsync(adminUsername);
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = adminUsername,
                    Email = "admin@local.test",
                    EmailConfirmed = true,
                    FullName = "Admin RAFLy"
                };

                var create = await userManager.CreateAsync(admin, adminPassword);
                if (!create.Succeeded)
                {
                    var msg = string.Join(", ", create.Errors.Select(e => e.Description));
                    throw new Exception($"Gagal membuat admin default: {msg}");
                }
            }

            if (!await userManager.IsInRoleAsync(admin, "Admin"))
                await userManager.AddToRoleAsync(admin, "Admin");
        }

        private static string GetRequiredSecret(IConfiguration config, string key, string errorMessage)
        {
            var value = config[key];

            if (string.IsNullOrWhiteSpace(value))
                value = Environment.GetEnvironmentVariable(key);

            if (string.IsNullOrWhiteSpace(value))
                throw new Exception(errorMessage);

            return value;
        }
    }
}
