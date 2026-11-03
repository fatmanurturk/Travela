using Microsoft.EntityFrameworkCore;
using Travela.Data;

namespace Travela.Api.Trips;

public sealed class TripLifecycleService(TravelaDbContext db, TimeProvider clock)
{
    private static readonly string[] ManagedStatuses = ["Scheduled", "Planlandı", "Planlandi", "InProgress", "In Progress", "Devam Ediyor", "Completed", "Tamamlandı", "Tamamlandi"];

    public Task<int> Synchronize(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // One conditional update is safe with concurrent edits/cancellations and multiple workers.
        // Cancelled records and their history are never part of this update.
        return db.Trips.Where(t => ManagedStatuses.Contains(t.Status) &&
            t.Status != (t.ArrivalTime <= now ? "Completed" : t.DepartureTime <= now ? "InProgress" : "Scheduled"))
            .ExecuteUpdateAsync(update => update
                .SetProperty(t => t.Status, t => t.ArrivalTime <= now ? "Completed" : t.DepartureTime <= now ? "InProgress" : "Scheduled")
                .SetProperty(t => t.UpdatedAt, now), ct);
    }
}
