using Microsoft.EntityFrameworkCore;
using Travela.Models;
using Travela.Api.Models;

namespace Travela.Data;

public partial class TravelaDbContext
{
    public DbSet<CompanyStaff> CompanyStaff => Set<CompanyStaff>();
    public DbSet<TripCrew> TripCrews => Set<TripCrew>();

    private static void ConfigureStaff(ModelBuilder model)
    {
        model.Entity<CompanyStaff>(e => {
            e.ToTable("CompanyStaff", t => t.HasCheckConstraint("CK_CompanyStaff_Duty", "[Duty] IN ('Driver','Attendant')"));
            e.HasKey(s => s.Id);
            e.HasAlternateKey(s => new { s.Id, s.CompanyId });
            e.Property(s => s.FullName).HasMaxLength(120);
            e.Property(s => s.Duty).HasMaxLength(16);
            e.Property(s => s.Phone).HasMaxLength(20);
            e.HasIndex(s => new { s.CompanyId, s.IsActive, s.Duty });
            e.HasOne<Company>().WithMany().HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<TripCrew>(e => {
            e.ToTable("TripCrews", t => t.HasCheckConstraint("CK_TripCrews_Duty", "[Duty] IN ('Driver','Attendant')"));
            e.HasKey(c => new { c.TripId, c.StaffId });
            e.Property(c => c.Duty).HasMaxLength(16);
            e.HasOne(c => c.Trip).WithMany().HasForeignKey(c => c.TripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(c => c.Staff).WithMany().HasForeignKey(c => new { c.StaffId, c.CompanyId })
                .HasPrincipalKey(s => new { s.Id, s.CompanyId }).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
