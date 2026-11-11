using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;

namespace Travela.Data;

public partial class TravelaDbContext
{
    public DbSet<RouteStop> RouteStops { get; set; }
    public DbSet<TripStop> TripStops { get; set; }
    public DbSet<TripLeg> TripLegs { get; set; }
    public DbSet<TripSegmentFare> TripSegmentFares { get; set; }
    public DbSet<ReservationLeg> ReservationLegs { get; set; }

    private static void ConfigureSegments(ModelBuilder model)
    {
        model.Entity<RouteStop>(e =>
        {
            e.HasKey(s => new { s.RouteId, s.StopOrder });
            e.Property(s => s.City).HasMaxLength(100);
            e.Property(s => s.DistanceFromStartKm).HasPrecision(12, 3);
            e.HasIndex(s => new { s.RouteId, s.City }).IsUnique();
            e.HasOne<Travela.Api.Models.Route>().WithMany().HasForeignKey(s => s.RouteId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Terminal>().WithMany().HasForeignKey(s => s.TerminalId).OnDelete(DeleteBehavior.NoAction);
            e.ToTable(t => t.HasCheckConstraint("CK_RouteStops_OrderTime", "[StopOrder] >= 1 AND [ArrivalOffsetMinutes] >= 0 AND [DepartureOffsetMinutes] >= [ArrivalOffsetMinutes] AND ([DistanceFromStartKm] IS NULL OR [DistanceFromStartKm] >= 0)"));
        });
        model.Entity<TripStop>(e =>
        {
            e.HasKey(s => new { s.TripId, s.StopOrder });
            e.Property(s => s.City).HasMaxLength(100);
            e.Property(s => s.TerminalName).HasMaxLength(160);
            e.Property(s => s.TerminalAddress).HasMaxLength(640);
            e.Property(s => s.BoardingInstructions).HasMaxLength(1000);
            e.Property(s => s.DistanceFromStartKm).HasPrecision(12, 3);
            e.HasIndex(s => new { s.TripId, s.City }).IsUnique();
            e.HasOne(s => s.Trip).WithMany(t => t.Stops).HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Terminal>().WithMany().HasForeignKey(s => s.TerminalId).OnDelete(DeleteBehavior.NoAction);
            e.ToTable(t => { t.UseSqlOutputClause(false); t.HasTrigger("TR_TripStops_Topology");
                t.HasCheckConstraint("CK_TripStops_OrderTime", "[StopOrder] >= 1 AND [DepartureTime] >= [ArrivalTime] AND ([DistanceFromStartKm] IS NULL OR [DistanceFromStartKm] >= 0)"); });
        });
        model.Entity<TripLeg>(e =>
        {
            e.HasKey(l => new { l.TripId, l.LegNumber });
            e.HasOne(l => l.Trip).WithMany(t => t.Legs).HasForeignKey(l => l.TripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripStop>().WithMany().HasForeignKey(l => new { l.TripId, l.FromStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripStop>().WithMany().HasForeignKey(l => new { l.TripId, l.ToStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.ToTable(t => { t.UseSqlOutputClause(false); t.HasTrigger("TR_TripLegs_Topology");
                t.HasCheckConstraint("CK_TripLegs_Adjacent", "[LegNumber] = [FromStopOrder] AND [ToStopOrder] = [FromStopOrder] + 1"); });
        });
        model.Entity<TripSegmentFare>(e =>
        {
            e.HasKey(f => new { f.TripId, f.FromStopOrder, f.ToStopOrder });
            e.Property(f => f.Amount).HasPrecision(18, 2);
            e.Property(f => f.Source).HasMaxLength(20);
            e.HasOne(f => f.Trip).WithMany(t => t.SegmentFares).HasForeignKey(f => f.TripId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripStop>().WithMany().HasForeignKey(f => new { f.TripId, f.FromStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripStop>().WithMany().HasForeignKey(f => new { f.TripId, f.ToStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.ToTable(t => t.HasCheckConstraint("CK_TripSegmentFares_Value", "[FromStopOrder] < [ToStopOrder] AND [Amount] >= 0.01 AND [Source] IN (N'FullRoute', N'Manual', N'Automatic') AND ([IsApproved] = 0 OR [ApprovedAt] IS NOT NULL)"));
        });
        model.Entity<Reservation>(e =>
        {
            e.HasAlternateKey(r => new { r.Id, r.TripId, r.SeatNumber });
            e.HasOne<TripStop>().WithMany().HasForeignKey(r => new { r.TripId, r.BoardingStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripStop>().WithMany().HasForeignKey(r => new { r.TripId, r.AlightingStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.ToTable(t => { t.UseSqlOutputClause(false); t.HasTrigger("TR_Reservations_Legs");
                t.HasCheckConstraint("CK_Reservations_Segment", "([BoardingStopOrder] IS NULL AND [AlightingStopOrder] IS NULL AND [BoardingDepartureSnapshot] IS NULL AND [AlightingArrivalSnapshot] IS NULL) OR ([BoardingStopOrder] IS NOT NULL AND [AlightingStopOrder] IS NOT NULL AND [BoardingStopOrder] < [AlightingStopOrder] AND [BoardingDepartureSnapshot] IS NOT NULL AND [AlightingArrivalSnapshot] > [BoardingDepartureSnapshot])"); });
        });
        model.Entity<PurchaseGroup>(e =>
        {
            e.HasOne<TripStop>().WithMany().HasForeignKey(g => new { g.TripId, g.BoardingStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripStop>().WithMany().HasForeignKey(g => new { g.TripId, g.AlightingStopOrder }).OnDelete(DeleteBehavior.NoAction);
            e.ToTable(t => { t.UseSqlOutputClause(false); t.HasTrigger("TR_PurchaseGroups_Segment"); });
        });
        model.Entity<PaymentOrder>().ToTable(t => { t.UseSqlOutputClause(false); t.HasTrigger("TR_PaymentOrders_Segment"); });
        model.Entity<ReservationLeg>(e =>
        {
            e.HasKey(l => new { l.ReservationId, l.LegNumber });
            e.HasOne(l => l.Reservation).WithMany().HasForeignKey(l => new { l.ReservationId, l.TripId, l.SeatNumber })
                .HasPrincipalKey(r => new { r.Id, r.TripId, r.SeatNumber }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<TripLeg>().WithMany().HasForeignKey(l => new { l.TripId, l.LegNumber }).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(l => new { l.TripId, l.SeatNumber, l.LegNumber }).IsUnique().HasFilter("[IsActive] = 1");
            e.ToTable(t => { t.UseSqlOutputClause(false); t.HasTrigger("TR_ReservationLegs_Consistency"); });
        });
    }

    // Bootstrap the unchanged two-endpoint writers, including maintenance/test callers.
    // Intermediate topology is explicitly supplied by TripSegmentService, never inferred from a later route edit.
    private async Task PrepareNewTripSnapshots(CancellationToken ct)
    {
        foreach (var trip in ChangeTracker.Entries<Trip>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList())
        {
            if (trip.Stops.Count != 0) continue;
            var route = trip.Route ?? await Routes.FindAsync([trip.RouteId], ct)
                ?? throw new InvalidOperationException("Seferin kayıtlı güzergâhı bulunamadı.");
            TripSegmentService.InitializeFullJourney(trip, route);
        }
        foreach (var reservation in ChangeTracker.Entries<Reservation>().Where(e => e.State == EntityState.Added && e.Entity.BoardingStopOrder == null).Select(e => e.Entity).ToList())
        {
            var stops = reservation.Trip?.Stops;
            if (stops is not { Count: >= 2 }) stops = await TripStops.Where(s => s.TripId == reservation.TripId).OrderBy(s => s.StopOrder).ToListAsync(ct);
            if (stops.Count >= 2) TripSegmentService.Capture(reservation, new(stops.MinBy(s => s.StopOrder)!, stops.MaxBy(s => s.StopOrder)!, reservation.Price, true));
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await PrepareNewTripSnapshots(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareNewTripSnapshots(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
}
