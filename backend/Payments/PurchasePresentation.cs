using Microsoft.EntityFrameworkCore;
using Travela.Api.Payments;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Api.Legal;
namespace Travela.Api.Payments;
public static class PurchasePresentation
{
    public static async Task<PurchaseDto> Detail(TravelaDbContext db, int userId, Guid id, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PaymentOrders.AsNoTracking().Include(p => p.Reservation).ThenInclude(r => r.Trip).ThenInclude(t => t.Route)
            .Include(p => p.Reservation).ThenInclude(r => r.Trip).ThenInclude(t => t.Company)
            .Include(p => p.Reservation).ThenInclude(r => r.Trip).ThenInclude(t => t.Bus)
            .SingleAsync(p => p.Id == id && p.PassengerId == userId, ct);
        var r = p.Reservation; var t = r.Trip;
        var legalAccepted = await db.LegalAcceptances.AsNoTracking().AnyAsync(a =>
            a.UserId == userId && a.DocumentType == LegalDocuments.Sales &&
            (p.PurchaseGroupId.HasValue ? a.PurchaseGroupId == p.PurchaseGroupId : a.PaymentOrderId == id), ct);
        var j = r.TicketJourneyJson == null ? TicketJourney.Capture(t) : TicketJourney.Read(r.TicketJourneyJson);
        DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return new() { Id = id, PurchaseGroupId = p.PurchaseGroupId, Pnr = r.Pnr, Kind = r.Kind, TripId = t.Id, SeatNumber = r.SeatNumber,
            CompanyName = j.CompanyName, BusPlateNumber = j.BusPlateNumber, PassengerName = r.PassengerNameSnapshot ?? "Yolcu bilgisi kaydedilmemiş",
            PassengerEmail = r.PassengerEmailSnapshot, PassengerPhone = r.PassengerPhoneSnapshot, PassengerGender = r.PassengerGender,
            PassengerCapturedAt = r.PassengerCapturedAt.HasValue ? Utc(r.PassengerCapturedAt.Value) : null, PassengerSnapshotSource = r.PassengerSnapshotSource,
            DepartureCity = j.DepartureCity, ArrivalCity = j.ArrivalCity,
            DepartureTerminalId = j.DepartureTerminalId, ArrivalTerminalId = j.ArrivalTerminalId, DepartureTerminalName = j.DepartureTerminalName, DepartureTerminalAddress = j.DepartureTerminalAddress, ArrivalTerminalName = j.ArrivalTerminalName, ArrivalTerminalAddress = j.ArrivalTerminalAddress, BoardingInstructions = j.BoardingInstructions,
            ArrivalTime = Utc(j.ArrivalTime), PaidAt = p.PaidAt.HasValue ? Utc(p.PaidAt.Value) : null, RetrievedAt = clock.GetUtcNow().UtcDateTime,
            DepartureTime = Utc(j.DepartureTime), CancelDeadline = Utc(r.ChangeCancellationUntil ?? j.DepartureTime.AddHours(-2)),
            ChangeCancellationUntil = r.ChangeCancellationUntil.HasValue ? Utc(r.ChangeCancellationUntil.Value) : null,
            HoldExpiresAt = r.HoldExpiresAt.HasValue ? Utc(r.HoldExpiresAt.Value) : null,
            Amount = p.Amount, OriginalAmount = p.OriginalAmount, DiscountAmount = p.DiscountAmount, CampaignName = p.CampaignName, CouponCode = p.CouponCode,
            Currency = p.Currency, IsTest = p.IsTest, Provider = p.Provider, PaymentStatus = p.PaymentStatus,
            TicketStatus = r.Status, RefundStatus = p.RefundStatus, RefundAmount = p.RefundAmount,
            CanCancel = r.Kind == "Ticket" && r.Status == "Active" && p.PaymentStatus == "Paid" && (PaymentService.CanCancel(t.DepartureTime, clock.GetUtcNow().UtcDateTime) || (r.ChangeCancellationUntil > clock.GetUtcNow().UtcDateTime && t.DepartureTime > clock.GetUtcNow().UtcDateTime)),
            CancellationReason = r.CancellationReason, CancelledAt = r.CancelledAt.HasValue ? Utc(r.CancelledAt.Value) : null,
            LastError = p.LastError?.Replace("Yerel test adaptörü iade hatası.", "İade işlemi tamamlanamadı."), NeedsAttention = p.NeedsAttention,
            LegalAccepted = legalAccepted,
            CheckoutUrl = legalAccepted && r.Kind == "Hold" && r.Status == "Active" && r.HoldExpiresAt > clock.GetUtcNow().UtcDateTime ? p.CheckoutUrl : null };
    }
}
