using System.Data;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;

namespace Travela.Api.Payments;

public class PaymentFlowException(string message, int status = 409) : Exception(message) { public int Status { get; } = status; }

public class PaymentService(TravelaDbContext db, IPaymentProvider provider, TimeProvider clock)
{
    public static bool CanCancel(DateTime departureUtc, DateTime nowUtc) => departureUtc - nowUtc >= TimeSpan.FromHours(2);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private Task<Trip> LockTrip(int id, CancellationToken ct) => db.Trips
        .FromSqlInterpolated($"SELECT * FROM Trips WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
        .Include(t => t.Company).Include(t => t.Bus).Include(t => t.Route).SingleAsync(ct);
    private static bool Scheduled(Trip trip) => trip.Status.ToLowerInvariant() is "scheduled" or "planlandı" or "planlandi";

    public async Task<PaymentOrder> Start(int userId, int tripId, int seat, Guid requestKey, CancellationToken ct)
    {
        if (requestKey == Guid.Empty || seat < 1) throw new PaymentFlowException("Geçerli koltuk ve işlem anahtarı gereklidir.", 400);
        if (!await db.Trips.AnyAsync(t => t.Id == tripId, ct)) throw new PaymentFlowException("Sefer bulunamadı.", 404);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var trip = await LockTrip(tripId, ct);
            var existing = await db.PaymentOrders.Include(p => p.Reservation).SingleOrDefaultAsync(p => p.PassengerId == userId && p.RequestKey == requestKey, ct);
            if (existing != null)
            {
                if (existing.Reservation.TripId != tripId || existing.Reservation.SeatNumber != seat)
                    throw new PaymentFlowException("İşlem anahtarı farklı bir sipariş için kullanılmış.");
                await tx.CommitAsync(ct);
                await tx.DisposeAsync();
                return await EnsureCheckout(existing, ct);
            }
            await ExpireLocked(trip, ct);
            if (!Scheduled(trip) || trip.DepartureTime <= Now || !trip.Company.IsActive || !trip.Bus.IsActive || !trip.Route.IsActive || trip.Bus.CompanyId != trip.CompanyId)
                throw new PaymentFlowException("Bu sefer bilet satışına açık değil.");
            if (seat > trip.Bus.SeatCapacity) throw new PaymentFlowException("Koltuk numarası kapasite dışında.", 400);
            var used = await db.Reservations.CountAsync(r => r.TripId == tripId && r.Status == "Active", ct);
            if (trip.AvailableSeats != trip.Bus.SeatCapacity - used) throw new PaymentFlowException("Eski kontenjan bilgileri doğrulanmadan satış yapılamaz.");
            if (trip.AvailableSeats <= 0 || await db.Reservations.AnyAsync(r => r.TripId == tripId && r.SeatNumber == seat && r.Status == "Active", ct))
                throw new PaymentFlowException("Koltuk başka bir yolcu tarafından alındı veya ödeme için tutuluyor.");
            var hold = new Reservation { TripId = tripId, PassengerId = userId, SeatNumber = seat, Price = trip.Price,
                Kind = "Hold", HoldExpiresAt = new[] { Now.AddMinutes(10), trip.DepartureTime }.Min() };
            var order = new PaymentOrder { PassengerId = userId, Reservation = hold, RequestKey = requestKey, Amount = trip.Price, Provider = provider.Name, IsTest = provider.IsTest };
            db.PaymentOrders.Add(order); trip.AvailableSeats--;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            await tx.DisposeAsync();
            return await EnsureCheckout(order, ct);
        }
    }

    public async Task<PaymentOrder> EnsureCheckout(PaymentOrder order, CancellationToken ct)
    {
        if (order.CheckoutUrl == null && order.PaymentStatus == "Pending")
        {
            var url = await provider.OpenCheckout(order.Id, order.Amount, order.Currency, ct);
            order.CheckoutUrl = url;
            await db.SaveChangesAsync(ct);
        }
        return order;
    }

    public async Task HandleNotice(ProviderNotice notice, string signature, CancellationToken ct)
    {
        if (notice.EventId.Length > 100 || !provider.Verify(notice, signature)) throw new PaymentFlowException("Ödeme bildirimi doğrulanamadı.", 400);
        var result = await provider.GetPayment(notice.OrderId, ct);
        await ApplyVerified(result, notice.EventId, ct);
    }

    // Only server-to-provider inquiry results reach this method; browser return URLs cannot issue tickets.
    public async Task Reconcile(Guid id, CancellationToken ct)
    {
        var order = await db.PaymentOrders.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        if (order.CheckoutUrl == null)
        {
            var url = await provider.OpenCheckout(order.Id, order.Amount, order.Currency, ct);
            await db.PaymentOrders.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CheckoutUrl, url), ct);
        }
        await ApplyVerified(await provider.GetPayment(id, ct), null, ct);
    }

    private async Task ApplyVerified(ProviderPayment result, string? eventId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var tripId = await db.PaymentOrders.Where(p => p.Id == result.OrderId).Select(p => p.Reservation.TripId).SingleAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await LockTrip(tripId, ct);
        var order = await db.PaymentOrders.Include(p => p.Reservation).SingleAsync(p => p.Id == result.OrderId, ct);
        if (order.Provider != provider.Name || order.Amount != result.Amount || order.Currency != result.Currency || order.IsTest != result.IsTest || string.IsNullOrEmpty(result.Reference))
            throw new PaymentFlowException("Sağlayıcı ödeme tutarı, para birimi veya ortamı eşleşmiyor.", 400);
        if (eventId != null && await db.PaymentNotifications.AnyAsync(n => n.Provider == provider.Name && n.EventId == eventId, ct)) return;
        if (result.Status == "Pending") return;
        var r = order.Reservation;
        if (result.Status == "Succeeded" && order.PaymentStatus != "Paid")
        {
            order.PaymentStatus = "Paid"; order.PaidAt = Now; order.ProviderReference = result.Reference;
            if (r.Kind == "Hold" && r.Status == "Active" && r.HoldExpiresAt > Now && trip.DepartureTime > Now && Scheduled(trip) && trip.Company.IsActive && trip.Bus.IsActive && trip.Route.IsActive)
            {
                r.Kind = "Ticket"; r.Pnr = Guid.NewGuid().ToString("N").ToUpperInvariant();
            }
            else
            {
                Release(r, trip, "Ödeme doğrulandı ancak bilet düzenlenemedi; tam iade başlatıldı.");
                RequestRefund(order, Now);
                order.NeedsAttention = true; order.LastError = "Ödeme sonrası bilet düzenlenemedi; telafi iadesi takip ediliyor.";
            }
        }
        else if (result.Status == "Failed" && order.PaymentStatus != "Paid")
        {
            order.PaymentStatus = "Failed";
            Release(r, trip, "Ödeme başarısız. Bilet düzenlenmedi.");
        }
        if (eventId != null) db.PaymentNotifications.Add(new() { Provider = provider.Name, EventId = eventId, OrderId = order.Id, ProcessedAt = Now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private void Release(Reservation r, Trip trip, string reason)
    {
        if (r.Status != "Active") return;
        r.Status = "Cancelled"; r.CancelledAt = Now; r.CancellationReason = reason; trip.AvailableSeats++;
    }

    public static void RequestRefund(PaymentOrder order, DateTime now)
    {
        if (order.PaymentStatus != "Paid" || order.RefundStatus != "None") return;
        order.RefundStatus = "Pending"; order.RefundAmount = order.Amount; order.NextRefundAttemptAt = now;
    }

    public async Task CancelTicket(int userId, string pnr, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var tripId = await db.Reservations.Where(r => r.Pnr == pnr && r.PassengerId == userId && r.Kind == "Ticket").Select(r => (int?)r.TripId).SingleOrDefaultAsync(ct);
        if (tripId == null) throw new PaymentFlowException("Bilet bulunamadı.", 404);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var trip = await LockTrip(tripId.Value, ct);
        var order = await db.PaymentOrders.Include(p => p.Reservation).SingleAsync(p => p.Reservation.Pnr == pnr && p.PassengerId == userId, ct);
        if (order.Reservation.Status == "Active")
        {
            if (!CanCancel(trip.DepartureTime, Now)) throw new PaymentFlowException("Kalkışa 2 saatten az kaldığı için iptal ve iade yapılamaz.");
            Release(order.Reservation, trip, "Yolcu tarafından iptal edildi. Tam iade süreci başlatıldı.");
            RequestRefund(order, Now);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task ExpireLocked(Trip trip, CancellationToken ct)
    {
        var expired = await db.Reservations.Where(r => r.TripId == trip.Id && r.Kind == "Hold" && r.Status == "Active" && r.HoldExpiresAt <= Now).ToListAsync(ct);
        foreach (var r in expired) Release(r, trip, "Ödeme süresi doldu. Bilet düzenlenmedi.");
        await db.SaveChangesAsync(ct);
    }
    public async Task ExpireHolds(CancellationToken ct)
    {
        var ids = await db.Reservations.AsNoTracking().Where(r => r.Kind == "Hold" && r.Status == "Active" && r.HoldExpiresAt <= Now).Select(r => r.TripId).Distinct().Take(100).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await ExpireLocked(await LockTrip(id, ct), ct); await tx.CommitAsync(ct);
        }
    }

    public async Task ProcessRefund(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var tripId = await db.PaymentOrders.Where(p => p.Id == id).Select(p => p.Reservation.TripId).SingleAsync(ct);
        var lease = Guid.NewGuid();
        PaymentOrder order;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await LockTrip(tripId, ct);
            order = await db.PaymentOrders.SingleAsync(p => p.Id == id, ct);
            if (order.RefundStatus is "None" or "Succeeded" || order.RefundLeaseUntil > Now || order.NextRefundAttemptAt > Now || order.RefundAttempts >= 8) return;
            order.RefundLeaseUntil = Now.AddMinutes(2); order.RefundLeaseToken = lease; order.RefundAttempts++;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        ProviderRefund result;
        try { result = await provider.Refund(order.Id, order.RefundAmount, order.RefundKey, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { result = new("Failed", null, "Sağlayıcı yanıtı alınamadı; aynı işlem anahtarıyla yeniden denenecek."); }
        if (result.Status == "Succeeded" && string.IsNullOrWhiteSpace(result.Reference))
            result = new("Failed", null, "Sağlayıcı iade referansı doğrulanamadı; takip gerekiyor.");
        db.ChangeTracker.Clear();
        await using var finish = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockTrip(tripId, ct);
        order = await db.PaymentOrders.SingleAsync(p => p.Id == id, ct);
        if (order.RefundLeaseToken != lease) return;
        order.RefundStatus = result.Status is "Succeeded" or "Pending" ? result.Status : "Failed";
        order.RefundReference = result.Reference; order.LastError = result.Error;
        order.RefundLeaseUntil = null; order.RefundLeaseToken = null;
        order.NextRefundAttemptAt = order.RefundStatus == "Succeeded" ? null : Now.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, order.RefundAttempts)));
        if (order.RefundStatus == "Succeeded") { order.RefundedAt = Now; order.NeedsAttention = false; }
        else if (order.RefundAttempts >= 8) order.NeedsAttention = true;
        await db.SaveChangesAsync(ct); await finish.CommitAsync(ct);
    }
}
