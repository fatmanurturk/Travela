using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Data;

internal static class TicketOperationsMigration
{
    public static async Task Apply(TravelaDbContext db)
    {
        const string migration = "20261104073635_AddTicketPassengerSnapshotsAndPaymentHistory";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0) { Console.WriteLine("Bilet operasyonları migration'ı zaten uygulanmış; veri değiştirilmedi."); return; }
        if (!pending.SequenceEqual(new[] { migration })) throw new InvalidOperationException("Bekleyen migration listesi farklı; otomatik geçiş yapılmadı.");
        async Task<string> ExistingData()
        {
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.CompanyId, u.IsActive, u.SecurityVersion, u.ActivationPending, u.PasswordResetRequired }).ToListAsync();
            var reservations = await db.Reservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.PassengerId, r.TripId, r.SeatNumber, r.Price, r.Pnr, r.Kind, r.Status, r.CreatedAt, r.IssuedAt, r.HoldExpiresAt, r.CancelledAt, r.CancellationReason }).ToListAsync();
            var orders = await db.PaymentOrders.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.ReservationId, p.PassengerId, p.Amount, p.Currency, p.PaymentStatus, p.ProviderReference, p.PaidAt, p.RefundStatus, p.RefundAmount, p.RefundKey, p.RefundReference, p.RefundedAt, p.RefundRequestedAt, p.RefundAttempts }).ToListAsync();
            return JsonSerializer.Serialize(new { users, reservations, orders, trips = await db.Trips.CountAsync() });
        }
        var before = await ExistingData();
        await db.Database.MigrateAsync();
        if (before != await ExistingData()) throw new InvalidOperationException("Geçiş sonrası mevcut kayıtlar farklı; eş zamanlı işlemleri inceleyin.");
        if (await db.Reservations.AnyAsync(r => r.PassengerCapturedAt == null || r.PassengerSnapshotSource != "Migration" ||
                r.PassengerNameSnapshot != r.Passenger.FullName || r.PassengerEmailSnapshot != r.Passenger.Email || r.PassengerPhoneSnapshot != r.Passenger.PhoneNumber))
            throw new InvalidOperationException("Eski yolcu kayıtlarının sabitlenmesi doğrulanamadı.");
        if (await db.PaymentEvents.CountAsync(e => e.Type == "ImportedState") != await db.PaymentOrders.CountAsync())
            throw new InvalidOperationException("Geçişte ödeme durumu kaydı eksik.");
        Console.WriteLine("Bilet operasyonları migration'ı uygulandı. Mevcut hesaplar, PNR'ler, koltuklar, tutarlar ve ödeme/iade kayıtları korundu; eski yolcu bilgileri Migration kaynağıyla sabitlendi.");
    }
}
