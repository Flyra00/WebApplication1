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
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<Inventory> InventoryItems { get; set; }
        public DbSet<DamageReport> DamageReports { get; set; }
        public DbSet<MemberProfile> MemberProfiles { get; set; }
        public DbSet<PendingMemberSignup> PendingMemberSignups { get; set; }
        public DbSet<PhoneOtpVerification> PhoneOtpVerifications { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Ingredient>().ToTable("ingredients");
            builder.Entity<Inventory>().ToTable("Inventory");
            builder.Entity<AppSetting>().ToTable("AppSettings");
            builder.Entity<Reservation>()
                .Property(reservation => reservation.DpPercentage)
                .HasPrecision(5, 2);

            builder.Entity<Reservation>()
                .Property(reservation => reservation.Source)
                .HasMaxLength(20)
                .HasDefaultValue(ReservationSources.Online);

            // TableSession → MemberUser (nullable FK)
            builder.Entity<TableSession>()
                .HasOne(session => session.MemberUser)
                .WithMany(user => user.MemberTableSessions)
                .HasForeignKey(session => session.MemberUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Reservation → Table (nullable FK)
            builder.Entity<Reservation>()
                .HasOne(reservation => reservation.Table)
                .WithMany()
                .HasForeignKey(reservation => reservation.TableId)
                .OnDelete(DeleteBehavior.Restrict);

            // Reservation → CustomerUser (nullable FK)
            builder.Entity<Reservation>()
                .HasOne(reservation => reservation.CustomerUser)
                .WithMany(user => user.CustomerReservations)
                .HasForeignKey(reservation => reservation.CustomerUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Reservation → TableSession (nullable FK)
            builder.Entity<Reservation>()
                .HasOne(reservation => reservation.TableSession)
                .WithOne()
                .HasForeignKey<Reservation>(reservation => reservation.TableSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Order → Reservation (nullable FK)
            builder.Entity<Order>()
                .HasOne(order => order.Reservation)
                .WithOne(reservation => reservation.Order)
                .HasForeignKey<Order>(order => order.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Order → TableSession (nullable FK)
            builder.Entity<Order>()
                .HasOne(order => order.TableSession)
                .WithMany(session => session.Orders)
                .HasForeignKey(order => order.TableSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Order → CustomerUser (nullable FK)
            builder.Entity<Order>()
                .HasOne(order => order.CustomerUser)
                .WithMany(user => user.CustomerOrders)
                .HasForeignKey(order => order.CustomerUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Payment>()
                .Property(payment => payment.Purpose)
                .HasMaxLength(40);

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

            builder.Entity<PendingMemberSignup>()
                .Property(signup => signup.Amount)
                .HasPrecision(16, 2);

            builder.Entity<PendingMemberSignup>()
                .Property(signup => signup.Status)
                .HasMaxLength(20)
                .HasDefaultValue(PendingMemberSignupStatuses.PendingPayment);

            builder.Entity<PendingMemberSignup>()
                .Property(signup => signup.MidtransReference)
                .HasMaxLength(100);
        }
    }
}
