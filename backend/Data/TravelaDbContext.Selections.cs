using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;

namespace Travela.Data;

public partial class TravelaDbContext
{
    public DbSet<SeatSelectionSession> SeatSelectionSessions { get; set; }
    private static void ConfigureSelections(ModelBuilder model)
    {
        model.Entity<SeatSelectionSession>(e => {
            e.Property(s => s.TokenHash).HasMaxLength(64); e.Property(s => s.IpHash).HasMaxLength(64);
            e.HasIndex(s => new { s.IpHash, s.ExpiresAt }); e.HasIndex(s => new { s.UserId, s.ExpiresAt });
            e.HasIndex(s => s.PurchaseGroupId).IsUnique().HasFilter("[PurchaseGroupId] IS NOT NULL");
            e.HasOne<Trip>().WithMany().HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<PurchaseGroup>().WithMany().HasForeignKey(s => s.PurchaseGroupId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<Reservation>().HasOne<SeatSelectionSession>().WithMany().HasForeignKey(r => r.SelectionSessionId).OnDelete(DeleteBehavior.NoAction);
        model.Entity<Reservation>().HasIndex(r => new { r.SelectionSessionId, r.Status, r.HoldExpiresAt });
    }
}
