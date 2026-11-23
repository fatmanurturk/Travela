using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;

namespace Travela.Data;

public partial class TravelaDbContext
{
    public DbSet<GuestCheckoutSession> GuestCheckoutSessions { get; set; }
    public DbSet<TicketCancellationChallenge> TicketCancellationChallenges { get; set; }
    private static void ConfigureGuestCheckout(ModelBuilder model)
    {
        model.Entity<GuestCheckoutSession>().HasIndex(s => s.ExpiresAt);
        model.Entity<PurchaseGroup>(e =>
        {
            e.HasOne<GuestCheckoutSession>().WithMany().HasForeignKey(g => g.GuestSessionId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(g => new { g.GuestSessionId, g.RequestKey }).IsUnique().HasFilter("[GuestSessionId] IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_PurchaseGroups_Owner", "([PassengerId] IS NOT NULL AND [GuestSessionId] IS NULL) OR ([PassengerId] IS NULL AND [GuestSessionId] IS NOT NULL)"));
        });
        model.Entity<TicketCancellationChallenge>(e =>
        {
            e.Property(c => c.GrantHash).HasMaxLength(64);
            e.Property(c => c.CodeHash).HasMaxLength(64);
            e.HasOne<PaymentOrder>().WithMany().HasForeignKey(c => c.PaymentOrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(c => new { c.PaymentOrderId, c.CreatedAt });
        });
    }
}
