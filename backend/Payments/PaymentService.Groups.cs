using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Api.Notifications;
using Travela.Api.Security;
using Travela.Api.Trips;
using Travela.DTOs;

namespace Travela.Api.Payments;

public partial class PaymentService
{
    public async Task<PurchaseGroup> StartGroup(int userId, StartGroupPurchaseDto dto, string? protectedBuyer, CancellationToken ct)
    {
        if (dto.RequestKey == Guid.Empty || dto.Passengers is not { Count: >= 1 and <= 10 } || dto.Passengers.Any(p => p == null || string.IsNullOrWhiteSpace(p.FullName) || string.IsNullOrWhiteSpace(p.Email)) || dto.Passengers.Select(p => p.SeatNumber).Distinct().Count() != dto.Passengers.Count)
            throw new PaymentFlowException("1–10 farklı koltuk ve her yolcu için geçerli bilgiler gereklidir.", 400);
        if (provider.Name == "IyzicoSandbox" && dto.Buyer == null) throw new PaymentFlowException("Ödeme için alıcı bilgileri gereklidir.", 400);
        var passengers = dto.Passengers.OrderBy(p => p.SeatNumber).Select(p => new { p.SeatNumber, name = p.FullName.Trim(), email = p.Email.Trim().ToLowerInvariant(), phone = p.Phone.Trim() }).ToList();
        var couponCode = CampaignPricing.NormalizeCode(dto.CouponCode);
        // Keep historical no-coupon request hashes compatible with already-created orders.
        var fingerprint = couponCode == null ? JsonSerializer.Serialize(new { dto.TripId, passengers, dto.Buyer })
            : JsonSerializer.Serialize(new { dto.TripId, passengers, dto.Buyer, couponCode });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        if (!await db.Trips.AnyAsync(t => t.Id == dto.TripId, ct)) throw new PaymentFlowException("Sefer bulunamadı.", 404);
        PurchaseGroup group;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var trip = await LockTrip(dto.TripId, ct);
            var existing = await db.PurchaseGroups.SingleOrDefaultAsync(g => g.PassengerId == userId && g.RequestKey == dto.RequestKey, ct);
            if (existing != null)
            {
                if (existing.RequestHash != hash) throw new PaymentFlowException("İşlem anahtarı farklı yolcu veya koltuk bilgileriyle kullanılmış.");
                await tx.CommitAsync(ct); await tx.DisposeAsync(); return await EnsureGroupCheckout(existing.Id, ct);
            }
            await ExpireLocked(trip, ct);
            if (!Scheduled(trip) || trip.DepartureTime <= Now || !trip.Company.IsActive || !trip.Bus.IsActive || trip.Bus.CompanyId != trip.CompanyId || !trip.Route.IsActive || trip.DepartureTerminal is { IsActive: false } || trip.ArrivalTerminal is { IsActive: false })
                throw new PaymentFlowException("Sefer satışa kapalı.");
            List<SeatPosition> plan;
            try { plan = SeatPlans.Read(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
            catch (ArgumentException) { throw new PaymentFlowException("Seferin koltuk planı doğrulanamadı; firma kontrolü gereklidir."); }
            if (passengers.Any(p => !plan.Any(s => s.Number == p.SeatNumber && !s.IsBlocked))) throw new PaymentFlowException("Seçilen koltuklardan biri geçersiz veya satışa kapalı.");
            var occupied = await db.Reservations.Where(r => r.TripId == trip.Id && r.Status == "Active").Select(r => r.SeatNumber).ToListAsync(ct);
            if (trip.AvailableSeats != (trip.SellableSeatCount ?? trip.Bus.SeatCapacity) - occupied.Count || trip.AvailableSeats < passengers.Count || passengers.Any(p => occupied.Contains(p.SeatNumber)))
                throw new PaymentFlowException("Seçilen koltuklardan biri artık alınamıyor. Hiçbir koltuk ayrılmadı; planı yenileyin.");
            if (!await db.Users.SessionAccounts(db).AnyAsync(u => u.Id == userId && u.Role == "Passenger", ct)) throw new PaymentFlowException("Aktif yolcu hesabı gereklidir.", 401);
            var price = await CampaignPricing.Quote(db, trip, userId, passengers.Count, couponCode, Now, true, ct);
            if (dto.ExpectedAmount.HasValue && dto.ExpectedAmount.Value != price.Amount)
                throw new PaymentFlowException("Fiyat veya kampanya koşulları değişti. Güncel toplamı kontrol edip tekrar deneyin.");
            group = new() { PassengerId = userId, TripId = trip.Id, RequestKey = dto.RequestKey, RequestHash = hash, Amount = price.Amount,
                OriginalAmount = price.OriginalAmount, DiscountAmount = price.DiscountAmount, CampaignId = price.CampaignId, CampaignName = price.CampaignName, CouponCode = price.CouponCode,
                Provider = provider.Name, IsTest = provider.IsTest, ProtectedBuyerJson = protectedBuyer, HoldExpiresAt = new[] { Now.AddMinutes(10), trip.DepartureTime }.Min() };
            var discountCents = (long)(price.DiscountAmount * 100);
            var index = 0;
            foreach (var p in passengers)
            {
                var lineDiscount = (discountCents / passengers.Count + (index++ < discountCents % passengers.Count ? 1 : 0)) / 100m;
                var originalAmount = decimal.Round(trip.Price, 2);
                var reservation = new Reservation { TripId = trip.Id, PassengerId = userId, SeatNumber = p.SeatNumber, Price = originalAmount - lineDiscount,
                    PassengerNameSnapshot = p.name, PassengerEmailSnapshot = p.email, PassengerPhoneSnapshot = p.phone, PassengerCapturedAt = Now, PassengerSnapshotSource = "PassengerEntry", Kind = "Hold", HoldExpiresAt = group.HoldExpiresAt };
                var line = new PaymentOrder { PassengerId = userId, PurchaseGroup = group, Reservation = reservation, RequestKey = Guid.NewGuid(), Amount = reservation.Price,
                    OriginalAmount = originalAmount, DiscountAmount = lineDiscount, CampaignName = price.CampaignName, CouponCode = price.CouponCode,
                    Provider = provider.Name, IsTest = provider.IsTest, CheckoutUrl = "/purchase/" + group.Id };
                group.Items.Add(line); PaymentHistory.Record(db, line, "CheckoutCreated", Now);
            }
            db.PurchaseGroups.Add(group); trip.AvailableSeats -= passengers.Count;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        return await EnsureGroupCheckout(group.Id, ct);
    }

    public async Task<PurchaseGroup> EnsureGroupCheckout(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        PurchaseGroup group;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            group = await db.PurchaseGroups.FromSqlInterpolated($"SELECT * FROM PurchaseGroups WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleAsync(ct);
            if (group.CheckoutState != "New" || group.PaymentStatus != "Pending" || group.HoldExpiresAt <= Now) return group;
            group.CheckoutState = "Initializing"; group.CheckoutLeaseUntil = Now.AddMinutes(2);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        try
        {
            var url = await ProviderFor(group.Provider).OpenCheckout(group.Id, group.Amount, group.Currency, ct);
            group.CheckoutUrl = url; group.CheckoutState = "Ready"; group.CheckoutLeaseUntil = null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { group.CheckoutState = "Unknown"; group.NeedsAttention = true; group.LastError = "Ödeme sayfası başlatma sonucu doğrulanamadı. Yeni ödeme başlatmadan sipariş durumunu kontrol edin."; group.CheckoutLeaseUntil = null; }
        await db.SaveChangesAsync(ct); return group;
    }

    public async Task ReconcileGroup(Guid id, CancellationToken ct, string? eventId = null)
    {
        db.ChangeTracker.Clear();
        var group = await db.PurchaseGroups.AsNoTracking().SingleAsync(g => g.Id == id, ct);
        if (group.PaymentStatus == "Paid") return;
        try
        {
            if (group.CheckoutState == "New" && group.HoldExpiresAt > Now) await EnsureGroupCheckout(id, ct);
            var source = ProviderFor(group.Provider);
            await ApplyGroupVerified(await source.GetPayment(id, ct), source.Name, eventId, ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.PurchaseGroups.Where(g => g.Id == id).ExecuteUpdateAsync(s => s.SetProperty(g => g.ReconciliationAttempts, g => g.ReconciliationAttempts + 1)
                .SetProperty(g => g.NeedsAttention, g => g.NeedsAttention || (g.PaymentStatus == "Pending" && g.ReconciliationAttempts >= 7))
                .SetProperty(g => g.NextReconciliationAt, g => g.PaymentStatus == "Pending" ? Now.AddMinutes(g.ReconciliationAttempts >= 8 ? 30 : 1) : (DateTime?)null), CancellationToken.None);
        }
    }

    private async Task ApplyGroupVerified(ProviderPayment result, string sourceName, string? eventId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var tripId = await db.PurchaseGroups.Where(g => g.Id == result.OrderId).Select(g => g.TripId).SingleAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await LockTrip(tripId, ct);
        var group = await db.PurchaseGroups.Include(g => g.Items).ThenInclude(p => p.Reservation).SingleAsync(g => g.Id == result.OrderId, ct);
        if (group.Provider != sourceName || group.Amount != result.Amount || group.Currency != result.Currency || group.IsTest != result.IsTest) throw new PaymentFlowException("Sağlayıcı sipariş, tutar veya ortam bilgisi uyuşmuyor.");
        if (group.PaymentStatus == "Paid") return;
        if (eventId != null && await db.PaymentNotifications.AnyAsync(n => n.Provider == sourceName && n.EventId == eventId, ct)) return;
        if (result.Status == "Pending") return;
        if (result.Status == "Succeeded" && (result.Lines == null || result.Lines.Count != group.Items.Count || result.Lines.Select(l => l.TransactionId).Distinct().Count() != group.Items.Count ||
            group.Items.Any(p => result.Lines.Count(l => l.OrderId == p.Id && l.Amount == p.Amount && !string.IsNullOrWhiteSpace(l.TransactionId)) != 1)))
            throw new PaymentFlowException("Sağlayıcı sepet satırları doğrulanamadı; otomatik bilet düzenlenmedi.");
        var valid = group.Items.All(p => p.Reservation.Kind == "Hold" && p.Reservation.Status == "Active" && p.Reservation.HoldExpiresAt > Now) && trip.DepartureTime > Now && Scheduled(trip) && trip.Company.IsActive && trip.Bus.IsActive && trip.Route.IsActive &&
            trip.DepartureTerminal is not { IsActive: false } && trip.ArrivalTerminal is not { IsActive: false };
        foreach (var p in group.Items)
        {
            var r = p.Reservation;
            if (result.Status == "Succeeded")
            {
                p.PaymentStatus = "Paid"; p.PaidAt = Now; p.ProviderReference = result.Lines!.Single(l => l.OrderId == p.Id).TransactionId;
                PaymentHistory.Record(db, p, "PaymentConfirmed", Now);
                if (valid)
                {
                    r.Kind = "Ticket"; r.Pnr = Guid.NewGuid().ToString("N").ToUpperInvariant(); r.IssuedAt = Now; r.TicketJourneyJson = TicketJourney.Capture(trip).Write();
                    PaymentHistory.Record(db, p, "TicketIssued", Now);
                    await TravelNotifications.Queue(db, r, "Purchase", "purchase:" + p.Id, Now, ct: ct);
                }
                else { Release(r, trip, "Ortak siparişin tüm biletleri düzenlenemedi; tam iade başlatıldı."); RequestRefund(p, Now, db); p.NeedsAttention = true; p.LastError = "Ortak sipariş için telafi iadesi takip ediliyor."; }
            }
            else { p.PaymentStatus = "Failed"; Release(r, trip, "Ortak sipariş ödemesi başarısız; hiçbir bilet düzenlenmedi."); PaymentHistory.Record(db, p, "PaymentFailed", Now); }
        }
        group.PaymentStatus = result.Status == "Succeeded" ? "Paid" : "Failed";
        if (result.Status == "Succeeded") group.ProviderPaymentId = result.Reference;
        group.ProtectedBuyerJson = null;
        if (eventId != null) db.PaymentNotifications.Add(new() { Provider = sourceName, EventId = eventId, OrderId = group.Id, ProcessedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
