using Microsoft.EntityFrameworkCore;
using Travela.Models;
using Travela.Api.Models;

namespace Travela.Data;

public partial class TravelaDbContext
{
    public DbSet<CompanyStaff> CompanyStaff => Set<CompanyStaff>();
    public DbSet<TripCrew> TripCrews => Set<TripCrew>();
    public DbSet<StaffPhoto> StaffPhotos => Set<StaffPhoto>();
    public DbSet<StaffDocument> StaffDocuments => Set<StaffDocument>();

    private static void ConfigureStaff(ModelBuilder model)
    {
        model.Entity<CompanyStaff>(e => {
            e.ToTable("CompanyStaff", t => t.HasCheckConstraint("CK_CompanyStaff_Duty", "[Duty] IN ('Driver','Attendant')"));
            e.HasKey(s => s.Id);
            e.HasAlternateKey(s => new { s.Id, s.CompanyId });
            e.Property(s => s.FullName).HasMaxLength(120);
            e.Property(s => s.Duty).HasMaxLength(16);
            e.Property(s => s.Phone).HasMaxLength(20);
            e.Property(s => s.EmergencyContactName).HasMaxLength(120);
            e.Property(s => s.EmergencyContactRelationship).HasMaxLength(80);
            e.Property(s => s.EmergencyContactPhone).HasMaxLength(20);
            e.Property(s => s.Languages).HasDefaultValueSql("N'[]'");
            e.Property(s => s.LicenseClasses).HasDefaultValueSql("N'[]'");
            e.Property(s => s.VehicleTypes).HasDefaultValueSql("N'[]'");
            e.Property(s => s.ServiceExperienceNotes).HasMaxLength(1000);
            e.Property(s => s.BaggageExperienceNotes).HasMaxLength(1000);
            e.Property(s => s.PassengerCommunicationExperienceNotes).HasMaxLength(1000);
            e.HasIndex(s => new { s.CompanyId, s.IsActive, s.Duty });
            e.HasOne<Company>().WithMany().HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<StaffPhoto>(e => {
            e.HasKey(p => p.StaffId);
            e.Property(p => p.ContentType).HasMaxLength(32);
            e.ToTable("StaffPhotos", t => t.HasCheckConstraint("CK_StaffPhotos_Size", "DATALENGTH([Data]) > 0 AND DATALENGTH([Data]) <= 5242880"));
            e.HasOne<CompanyStaff>().WithOne().HasForeignKey<StaffPhoto>(p => new { p.StaffId, p.CompanyId })
                .HasPrincipalKey<CompanyStaff>(s => new { s.Id, s.CompanyId }).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<StaffDocument>(e => {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(160);
            e.Property(d => d.DocumentType).HasMaxLength(32);
            e.Property(d => d.ContentType).HasMaxLength(32);
            e.Property(d => d.FileName).HasMaxLength(200);
            e.HasIndex(d => new { d.CompanyId, d.ExpiresOn });
            e.ToTable("StaffDocuments", t => {
                t.HasCheckConstraint("CK_StaffDocuments_Validity", "([IsIndefinite] = 1 AND [ExpiresOn] IS NULL) OR ([IsIndefinite] = 0 AND [ExpiresOn] IS NOT NULL AND [ExpiresOn] >= [IssuedOn])");
                t.HasCheckConstraint("CK_StaffDocuments_Size", "[Size] > 0 AND [Size] <= 10485760 AND DATALENGTH([Data]) = [Size]");
            });
            e.HasOne(d => d.Staff).WithMany().HasForeignKey(d => new { d.StaffId, d.CompanyId })
                .HasPrincipalKey(s => new { s.Id, s.CompanyId }).OnDelete(DeleteBehavior.Cascade);
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
