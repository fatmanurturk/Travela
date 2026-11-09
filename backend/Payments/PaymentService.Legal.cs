using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Legal;
using Travela.Api.Models;
using Travela.DTOs;

namespace Travela.Api.Payments;

public partial class PaymentService
{
    private LegalAcceptance PurchaseAcceptance(
        int userId, Trip trip, Guid requestKey, string ip, object financial)
    {
        var acceptance = LegalDocuments.Record(LegalDocuments.Sales, Now, ip);
        acceptance.UserId = userId;
        acceptance.Context = "Purchase";
        acceptance.RequestKey = requestKey;
        acceptance.TransactionSnapshot = JsonSerializer.Serialize(new
        {
            trip.Id,
            trip.CompanyId,
            companyName = trip.Company.Name,
            trip.RouteId,
            trip.Route.DepartureCity,
            trip.Route.ArrivalCity,
            trip.DepartureTime,
            trip.ArrivalTime,
            financial
        });
        return acceptance;
    }

    public Task<bool> HasLegalAcceptance(int userId, Guid id, bool group, CancellationToken ct) =>
        db.LegalAcceptances.AsNoTracking().AnyAsync(a => a.UserId == userId &&
            a.DocumentType == LegalDocuments.Sales &&
            (group ? a.PurchaseGroupId == id : a.PaymentOrderId == id), ct);

    // Pending historical orders require a new explicit acknowledgement; never backfill old consent.
    public async Task AcceptOrderLegal(
        int userId, Guid id, bool group, IReadOnlyList<LegalAcceptanceDto>? acceptances,
        string? clientIp, CancellationToken ct)
    {
        LegalDocuments.Validate(acceptances, LegalDocuments.Sales);
        var ip = LegalDocuments.RequireIp(clientIp);
        var tripId = group
            ? await db.PurchaseGroups.Where(g => g.Id == id && g.PassengerId == userId)
                .Select(g => (int?)g.TripId).SingleOrDefaultAsync(ct)
            : await db.PaymentOrders.Where(p => p.Id == id && p.PassengerId == userId)
                .Select(p => (int?)p.Reservation.TripId).SingleOrDefaultAsync(ct);
        if (tripId is null) throw new PaymentFlowException("Sipariş bulunamadı.", 404);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await LockTrip(tripId.Value, ct);
        if (!await HasLegalAcceptance(userId, id, group, ct))
        {
            LegalAcceptance acceptance;
            if (group)
            {
                var order = await db.PurchaseGroups.Include(g => g.Items).ThenInclude(p => p.Reservation)
                    .SingleAsync(g => g.Id == id && g.PassengerId == userId, ct);
                if (order.PaymentStatus != "Pending" || order.HoldExpiresAt <= Now)
                    throw new PaymentFlowException("Onay yalnızca süresi dolmamış, ödenmemiş siparişe eklenebilir.");
                acceptance = PurchaseAcceptance(userId, trip, order.RequestKey, ip,
                    new { order.Amount, order.Currency, seats = order.Items.Select(p => p.Reservation.SeatNumber).ToArray() });
                acceptance.PurchaseGroupId = id;
            }
            else
            {
                var order = await db.PaymentOrders.Include(p => p.Reservation)
                    .SingleAsync(p => p.Id == id && p.PassengerId == userId, ct);
                if (order.PaymentStatus != "Pending" || order.Reservation.HoldExpiresAt <= Now)
                    throw new PaymentFlowException("Onay yalnızca süresi dolmamış, ödenmemiş siparişe eklenebilir.");
                acceptance = PurchaseAcceptance(userId, trip, order.RequestKey, ip,
                    new { order.Amount, order.Currency, seats = new[] { order.Reservation.SeatNumber } });
                acceptance.PaymentOrderId = id;
            }
            db.LegalAcceptances.Add(acceptance);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
