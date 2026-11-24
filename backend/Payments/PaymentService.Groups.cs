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
using Travela.Api.Legal;

namespace Travela.Api.Payments;

public partial class PaymentService
{
    public async Task<PurchaseGroup> StartGroup(
        int? userId, StartGroupPurchaseDto dto, string? protectedBuyer,
        CancellationToken ct, string? clientIp = null, Guid? guestSessionId = null, string? selectionToken = null)
    {
        LegalDocuments.Validate(dto.LegalAcceptances, LegalDocuments.Sales);
        var consentIp = LegalDocuments.RequireIp(clientIp);
        if (userId.HasValue == guestSessionId.HasValue) throw new PaymentFlowException("Sipariş oturumu doğrulanamadı.", 403);
        if (dto.RequestKey == Guid.Empty || dto.Passengers is not { Count: >= 1 and <= 10 } || dto.Passengers.Any(p => p == null || string.IsNullOrWhiteSpace(p.FullName) || p.FullName.Trim().Length < 2) || dto.Passengers.Select(p => p.SeatNumber).Distinct().Count() != dto.Passengers.Count)
            throw new PaymentFlowException("1–10 farklı koltuk ve her yolcu için geçerli bilgiler gereklidir.", 400);
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(dto, new(dto), [], true))
            throw new PaymentFlowException("Ortak iletişim bilgileri eksik veya geçersiz. E-posta ve telefonu kontrol ediniz.", 400);
        if (provider.Name is "IyzicoSandbox" or "Iyzico" && dto.Buyer == null) throw new PaymentFlowException("Ödeme için alıcı bilgileri gereklidir.", 400);
        if (dto.Passengers.Any(p => !System.ComponentModel.DataAnnotations.Validator.TryValidateObject(p, new(p), [], true)))
            throw new PaymentFlowException("Yolcu bilgileri eksik veya geçersiz. Her yolcu için ad soyad, geçerli T.C. kimlik numarası ve cinsiyet seçiniz.", 400);
        var passengers = dto.Passengers.OrderBy(p => p.SeatNumber).Select(p => new { p.SeatNumber, name = p.FullName.Trim(), identityNumber = p.IdentityNumber, gender = p.Gender }).ToList();
        var contactEmail = BookingContact.Email(dto.ContactEmail);
        var contactPhone = BookingContact.Phone(dto.ContactPhone) ?? throw new PaymentFlowException("Geçerli bir cep telefonu giriniz.", 400);
        var couponCode = CampaignPricing.NormalizeCode(dto.CouponCode);
        var fingerprint = JsonSerializer.Serialize(new { dto.TripId, passengers, contactEmail, contactPhone, dto.Buyer, couponCode, dto.BoardingStopOrder, dto.AlightingStopOrder });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        if (!await db.Trips.AnyAsync(t => t.Id == dto.TripId, ct)) throw new PaymentFlowException("Sefer bulunamadı.", 404);
        PurchaseGroup group;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var trip = await LockTrip(dto.TripId, ct);
            SeatSelectionSession? selection = null;
            var held = new List<Reservation>();
            if (dto.SelectionSessionId is { } selectionId)
            {
                selection = await new SeatSelectionService(db, clock, Segments).Require(selectionId, selectionToken, ct, true);
                if (selection.TripId != trip.Id) throw new PaymentFlowException("Seçim farklı bir sefere ait.", 409, "selection_changed");
                if (selection.PurchaseGroupId is { } paymentId)
                {
                    var previous = await db.PurchaseGroups.SingleAsync(g => g.Id == paymentId, ct);
                    if (previous.PassengerId != userId || previous.GuestSessionId != guestSessionId || previous.RequestHash != hash)
                        throw new PaymentFlowException("Bu seçim için mevcut siparişinizden devam edin.", 409, "selection_in_payment");
                    await tx.CommitAsync(ct); await tx.DisposeAsync(); return await EnsureGroupCheckout(paymentId, ct);
                }
                if (selection.ExpiresAt == null || selection.ExpiresAt <= Now) throw SeatSelectionService.Expired();
                held = await db.Reservations.Where(r => r.SelectionSessionId == selectionId && r.Kind == "Hold" && r.Status == "Active" && r.HoldExpiresAt > Now).ToListAsync(ct);
                if (held.Count != passengers.Count || passengers.Any(p => !held.Any(r => r.SeatNumber == p.SeatNumber && r.PassengerGender == p.gender)))
                    throw new PaymentFlowException("Koltuk seçiminiz değişti. Seçiminizi sunucudan yeniden doğrulayın.", 409, "selection_changed");
            }
            var existing = await db.PurchaseGroups.SingleOrDefaultAsync(g => g.PassengerId == userId && g.GuestSessionId == guestSessionId && g.RequestKey == dto.RequestKey, ct);
            if (existing != null)
            {
                if (selection != null) throw new PaymentFlowException("Bu işlem başka bir satın alma oturumuna ait.", 409, "selection_changed");
                if (existing.RequestHash != hash) throw new PaymentFlowException("İşlem anahtarı farklı yolcu veya koltuk bilgileriyle kullanılmış.");
                await tx.CommitAsync(ct); await tx.DisposeAsync(); return await EnsureGroupCheckout(existing.Id, ct);
            }
            await ExpireLocked(trip, ct);
            var segment = await Segments.Resolve(trip, dto.BoardingStopOrder, dto.AlightingStopOrder, ct);
            if (selection != null && (selection.BoardingStopOrder != segment.From.StopOrder || selection.AlightingStopOrder != segment.To.StopOrder))
                throw new PaymentFlowException("Seçilen yolculuk kesimi değişti.", 409, "selection_changed");
            if (!await Segments.CanSell(trip, segment, ct))
                throw new PaymentFlowException("Sefer satışa kapalı.");
            List<SeatPosition> plan;
            try { plan = SeatPlans.ReadVerified(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
            catch (ArgumentException) { throw new PaymentFlowException("Seferin koltuk planı doğrulanamadı; firma kontrolü gereklidir."); }
            if (passengers.Any(p => !plan.Any(s => s.Number == p.SeatNumber && !s.IsBlocked))) throw new PaymentFlowException("Seçilen koltuklardan biri geçersiz veya satışa kapalı.");
            var compatibility = await Segments.ValidateSelection(trip, segment.From.StopOrder, segment.To.StopOrder,
                passengers.Select(p => new SeatPassenger(p.SeatNumber, p.gender)).ToList(), ct, held.Select(r => r.Id).ToList());
            if (compatibility != null) throw new PaymentFlowException(compatibility);
            var available = await Segments.AvailableSeatNumbers(trip, segment.From.StopOrder, segment.To.StopOrder, ct);
            if (passengers.Any(p => !available.Contains(p.SeatNumber) && !held.Any(r => r.SeatNumber == p.SeatNumber)))
                throw new PaymentFlowException("Seçilen koltuklardan biri artık alınamıyor. Hiçbir koltuk ayrılmadı; planı yenileyin.");
            if (userId.HasValue && !await db.Users.SessionAccounts(db).AnyAsync(u => u.Id == userId && u.Role == "Passenger", ct)) throw new PaymentFlowException("Aktif yolcu hesabı gereklidir.", 401);
            var price = await CampaignPricing.Quote(db, trip, userId, passengers.Count, couponCode, Now, true, ct, segment.Price, segment.From.DepartureTime);
            if (dto.ExpectedAmount.HasValue && dto.ExpectedAmount.Value != price.Amount)
                throw new PaymentFlowException("Fiyat veya kampanya koşulları değişti. Güncel toplamı kontrol edip tekrar deneyin.");
            if (selection != null && selection.ExpiresAt <= Now) throw SeatSelectionService.Expired();
            group = new() { PassengerId = userId, GuestSessionId = guestSessionId, TripId = trip.Id, RequestKey = dto.RequestKey, RequestHash = hash, Amount = price.Amount,
                ContactEmail = contactEmail, ContactPhone = contactPhone,
                BoardingStopOrder = segment.From.StopOrder, AlightingStopOrder = segment.To.StopOrder,
                OriginalAmount = price.OriginalAmount, DiscountAmount = price.DiscountAmount, CampaignId = price.CampaignId, CampaignName = price.CampaignName, CouponCode = price.CouponCode,
                Provider = provider.Name, IsTest = provider.IsTest, ProtectedBuyerJson = protectedBuyer, HoldExpiresAt = new[] { Now.AddMinutes(10), segment.From.DepartureTime }.Min() };
            var discountCents = (long)(price.DiscountAmount * 100);
            var index = 0;
            foreach (var p in passengers)
            {
                var lineDiscount = (discountCents / passengers.Count + (index++ < discountCents % passengers.Count ? 1 : 0)) / 100m;
                var originalAmount = decimal.Round(segment.Price, 2);
                var reservation = held.SingleOrDefault(r => r.SeatNumber == p.SeatNumber) ?? new Reservation { TripId = trip.Id, SeatNumber = p.SeatNumber, Kind = "Hold" };
                reservation.PassengerId = userId; reservation.Price = originalAmount - lineDiscount;
                reservation.PassengerNameSnapshot = p.name; reservation.PassengerIdentityNumberSnapshot = p.identityNumber;
                reservation.PassengerGender = p.gender; reservation.PassengerCapturedAt = Now; reservation.PassengerSnapshotSource = "PassengerEntry";
                reservation.HoldExpiresAt = group.HoldExpiresAt;
                TripSegmentService.Capture(reservation, segment);
                reservation.TicketJourneyJson = TicketJourney.Capture(trip, segment).Write();
                var line = new PaymentOrder { PassengerId = userId, PurchaseGroup = group, Reservation = reservation, RequestKey = Guid.NewGuid(), Amount = reservation.Price,
                    OriginalAmount = originalAmount, DiscountAmount = lineDiscount, CampaignName = price.CampaignName, CouponCode = price.CouponCode,
                    Provider = provider.Name, IsTest = provider.IsTest, CheckoutUrl = "/purchase/" + group.Id };
                group.Items.Add(line); PaymentHistory.Record(db, line, "CheckoutCreated", Now);
            }
            db.PurchaseGroups.Add(group);
            if (selection != null) selection.PurchaseGroupId = group.Id;
            var acceptance = PurchaseAcceptance(userId, trip, dto.RequestKey, consentIp,
                new { group.Amount, group.Currency, group.OriginalAmount, group.DiscountAmount,
                    seats = passengers.Select(p => p.SeatNumber).ToArray() });
            acceptance.PurchaseGroupId = group.Id;
            db.LegalAcceptances.Add(acceptance);
            await Segments.SaveInventory(trip, ct); await tx.CommitAsync(ct);
        }
        return await EnsureGroupCheckout(group.Id, ct);
    }

    public async Task<PurchaseGroup> EnsureGroupCheckout(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var tripId = await db.PurchaseGroups.Where(g => g.Id == id).Select(g => g.TripId).SingleAsync(ct);
        PurchaseGroup group;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var trip = await LockTrip(tripId, ct);
            group = await db.PurchaseGroups.FromSqlInterpolated($"SELECT * FROM PurchaseGroups WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleAsync(ct);
            if (group.CheckoutState != "New" || group.PaymentStatus != "Pending" || group.HoldExpiresAt <= Now) return group;
            if (!await HasLegalAcceptance(group.PassengerId, group.Id, true, ct)) return group;
            var reservations = await db.Reservations.Where(r => r.Payment!.PurchaseGroupId == id).ToListAsync(ct);
            if (!await SeatsStillValid(trip, reservations, ct))
                throw new PaymentFlowException("Koltuk uygunluğu değişti veya rezervasyon süresi doldu. Koltuk planını yenileyiniz.");
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
            if (group.CheckoutState == "New" && !await HasLegalAcceptance(group.PassengerId, id, true, ct))
                return;
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
        var valid = group.Items.All(p => p.Reservation.Kind == "Hold" && p.Reservation.Status == "Active" && p.Reservation.HoldExpiresAt > Now) &&
            await SeatsStillValid(trip, group.Items.Select(p => p.Reservation).ToList(), ct);
        foreach (var p in group.Items)
        {
            var r = p.Reservation;
            if (result.Status == "Succeeded")
            {
                p.PaymentStatus = "Paid"; p.PaidAt = Now; p.ProviderReference = result.Lines!.Single(l => l.OrderId == p.Id).TransactionId;
                PaymentHistory.Record(db, p, "PaymentConfirmed", Now);
                if (valid)
                {
                    r.Kind = "Ticket"; r.Pnr = Guid.NewGuid().ToString("N").ToUpperInvariant(); r.IssuedAt = Now; r.TicketJourneyJson ??= (await Segments.Journey(trip, r, ct)).Write();
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
        await Segments.SaveInventory(trip, ct); await tx.CommitAsync(ct);
    }
}
