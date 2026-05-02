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
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<Inventory> InventoryItems { get; set; }
        public DbSet<DamageReport> DamageReports { get; set; }
        public DbSet<MemberProfile> MemberProfiles { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Ingredient>().ToTable("ingredients");
            builder.Entity<Inventory>().ToTable("Inventory");

            // TableSession → MemberUser (nullable FK)
            builder.Entity<TableSession>()
                .HasOne(session => session.MemberUser)
                .WithMany(user => user.MemberTableSessions)
                .HasForeignKey(session => session.MemberUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Order → CustomerUser (nullable FK)
            builder.Entity<Order>()
                .HasOne(order => order.CustomerUser)
                .WithMany(user => user.CustomerOrders)
                .HasForeignKey(order => order.CustomerUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Payment → PaidByUser (nullable FK)
            builder.Entity<Payment>()
                .HasOne(p => p.PaidByUser)
                .WithMany()
                .HasForeignKey(p => p.PaidByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // DamageReport → ReportedByUser
            builder.Entity<DamageReport>()
                .HasOne(d => d.ReportedByUser)
                .WithMany()
                .HasForeignKey(d => d.ReportedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // DamageReport → InventoryItem
            builder.Entity<DamageReport>()
                .HasOne(d => d.InventoryItem)
                .WithMany()
                .HasForeignKey(d => d.InventoryItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // MemberProfile → ApplicationUser (1-to-1)
            builder.Entity<MemberProfile>()
                .HasOne(m => m.User)
                .WithOne(u => u.MemberProfile)
                .HasForeignKey<MemberProfile>(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
