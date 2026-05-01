using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<TableSession> TableSessions { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }

        public DbSet<Ingredient> Ingredients { get; set; }

        public DbSet<Inventory> InventoryItems { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Ingredient>().ToTable("ingredients");
            builder.Entity<Inventory>().ToTable("Inventory");

            builder.Entity<TableSession>()
                .HasOne(session => session.MemberUser)
                .WithMany(user => user.MemberTableSessions)
                .HasForeignKey(session => session.MemberUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Order>()
                .HasOne(order => order.CustomerUser)
                .WithMany(user => user.CustomerOrders)
                .HasForeignKey(order => order.CustomerUserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
