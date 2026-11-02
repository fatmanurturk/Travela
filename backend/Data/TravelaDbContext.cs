using Microsoft.EntityFrameworkCore;
using Travela.Models;
using Travela.Api.Models;

namespace Travela.Data
{
    public class TravelaDbContext : DbContext
    {
        public TravelaDbContext(
            DbContextOptions<TravelaDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Bus> Buses { get; set; }
       public DbSet<Travela.Api.Models.Route> Routes { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
      
      protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Reservation>(entity =>
    {
        entity.Property(r => r.ReservationNumber).HasMaxLength(32);
        entity.Property(r => r.Price).HasPrecision(18, 2);
        entity.Property(r => r.Status).HasMaxLength(20).HasDefaultValue("Active");
        entity.Property(r => r.CancellationReason).HasMaxLength(1000);
        entity.HasIndex(r => r.ReservationNumber).IsUnique();
        entity.HasIndex(r => new { r.TripId, r.SeatNumber }).IsUnique().HasFilter("[Status] = N'Active'");
        entity.HasIndex(r => new { r.PassengerId, r.CreatedAt });
        entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_SeatNumber", "[SeatNumber] > 0"));
        entity.ToTable(t => t.HasCheckConstraint("CK_Reservations_Status", "([Status] = N'Active' AND [CancelledAt] IS NULL) OR ([Status] = N'Cancelled' AND [CancelledAt] IS NOT NULL)"));
        entity.HasOne(r => r.Trip).WithMany().HasForeignKey(r => r.TripId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(r => r.Passenger).WithMany().HasForeignKey(r => r.PassengerId).OnDelete(DeleteBehavior.NoAction);
    });

    modelBuilder.Entity<User>()
        .HasIndex(u => u.Email)
        .IsUnique();

    modelBuilder.Entity<User>()
        .HasOne<Company>()
        .WithMany()
        .HasForeignKey(u => u.CompanyId)
        .OnDelete(DeleteBehavior.SetNull);

    modelBuilder.Entity<Trip>()
        .Property(t => t.Price)
        .HasPrecision(18, 2);

    modelBuilder.Entity<Trip>()
        .Property(t => t.CancellationReason)
        .HasMaxLength(500);

    modelBuilder.Entity<Trip>()
        .HasIndex(t => new { t.BusId, t.DepartureTime });

    modelBuilder.Entity<Bus>()
        .Property(b => b.PlateNumber)
        .HasMaxLength(20);

    modelBuilder.Entity<Bus>()
        .Property(b => b.NormalizedPlateNumber)
        .HasMaxLength(20);

    modelBuilder.Entity<Bus>()
        .HasIndex(b => b.NormalizedPlateNumber)
        .IsUnique();

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Company)
        .WithMany()
        .HasForeignKey(t => t.CompanyId)
        .OnDelete(DeleteBehavior.NoAction);

    modelBuilder.Entity<RolePermission>()
        .HasOne(rp => rp.Role)
        .WithMany()
        .HasForeignKey(rp => rp.RoleId)
        .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<RolePermission>()
        .HasOne(rp => rp.Permission)
        .WithMany()
        .HasForeignKey(rp => rp.PermissionId)
        .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<RolePermission>()
        .HasIndex(rp => new { rp.RoleId, rp.PermissionId })
        .IsUnique();

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Bus)
        .WithMany()
        .HasForeignKey(t => t.BusId)
        .OnDelete(DeleteBehavior.NoAction);

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Route)
        .WithMany()
        .HasForeignKey(t => t.RouteId)
        .OnDelete(DeleteBehavior.NoAction);

    modelBuilder.Entity<Trip>()
        .HasOne<User>()
        .WithMany()
        .HasForeignKey(t => t.CancelledByUserId)
        .OnDelete(DeleteBehavior.SetNull);
}
    }
}
