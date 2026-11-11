using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Trips;

namespace Travela.Api.Payments;

public partial class PaymentService
{
    private async Task ValidateSingleCheckout(PaymentOrder order, CancellationToken ct)
    {
        var tripId = await db.Reservations.Where(r => r.Id == order.ReservationId).Select(r => r.TripId).SingleAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var trip = await LockTrip(tripId, ct);
        var reservation = await db.Reservations.SingleAsync(r => r.Id == order.ReservationId, ct);
        if (!Scheduled(trip) || trip.DepartureTime <= Now || !trip.Company.IsActive || !trip.Bus.IsActive || !trip.Route.IsActive ||
            trip.DepartureTerminal is { IsActive: false } || trip.ArrivalTerminal is { IsActive: false } || !await SeatsStillValid(trip, [reservation], ct))
            throw new PaymentFlowException("Koltuk uygunluğu değişti veya rezervasyon süresi doldu. Koltuk planını yenileyiniz.");
        await tx.CommitAsync(ct);
    }

    private Task<List<SeatOccupancy>> ActiveSeats(int tripId, CancellationToken ct) => db.Reservations
        .Where(r => r.TripId == tripId && r.Status == "Active" && (r.Kind != "Hold" || r.HoldExpiresAt > Now))
        .Select(r => new SeatOccupancy(r.SeatNumber, r.PassengerGender, r.Kind, r.HoldExpiresAt)).ToListAsync(ct);

    // Caller holds the trip's UPDLOCK in a serializable transaction.
    private async Task<bool> SeatsStillValid(Trip trip, IReadOnlyList<Reservation> reservations, CancellationToken ct)
    {
        if (trip.Bus.CompanyId != trip.CompanyId || reservations.Any(r => r.Status != "Active" || r.HoldExpiresAt <= Now)) return false;
        List<SeatPosition> plan;
        try { plan = SeatPlans.ReadVerified(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
        catch (ArgumentException) { return false; }
        var ids = reservations.Select(r => r.Id).ToList();
        var occupied = await db.Reservations.Where(r => r.TripId == trip.Id && r.Status == "Active" && !ids.Contains(r.Id) && (r.Kind != "Hold" || r.HoldExpiresAt > Now))
            .Select(r => new SeatOccupancy(r.SeatNumber, r.PassengerGender, r.Kind, r.HoldExpiresAt)).ToListAsync(ct);
        return SeatCompatibility.Validate(plan, reservations.Select(r => new SeatPassenger(r.SeatNumber, r.PassengerGender)).ToList(), occupied, Now) == null;
    }
}
