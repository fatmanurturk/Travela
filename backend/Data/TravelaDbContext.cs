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
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
      
      protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

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
}
    }
}