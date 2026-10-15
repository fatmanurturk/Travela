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
      
      protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Trip>()
        .Property(t => t.Price)
        .HasPrecision(18, 2);

    modelBuilder.Entity<Trip>()
        .HasOne(t => t.Company)
        .WithMany()
        .HasForeignKey(t => t.CompanyId)
        .OnDelete(DeleteBehavior.NoAction);

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