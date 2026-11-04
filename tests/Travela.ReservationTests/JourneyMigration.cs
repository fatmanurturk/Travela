using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Data;

internal static class JourneyMigration
{
    public static async Task Apply(TravelaDbContext db)
    {
        const string migration = "20261104080859_AddSeatPlansAndTerminals";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0) { Console.WriteLine("Koltuk/terminal migration'ı zaten uygulanmış; veri değiştirilmedi."); return; }
        if (!pending.SequenceEqual(new[] { migration })) throw new InvalidOperationException("Bekleyen migration listesi farklı; otomatik geçiş yapılmadı.");
        async Task<string> ExistingData()
        {
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.CompanyId, u.IsActive, u.SecurityVersion, u.ActivationPending, u.PasswordResetRequired }).ToListAsync();
            var reservations = await db.Reservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.PassengerId, r.TripId, r.SeatNumber, r.Price, r.Pnr, r.Kind, r.Status, r.CreatedAt, r.IssuedAt, r.HoldExpiresAt, r.CancelledAt, r.CancellationReason, r.PassengerNameSnapshot, r.PassengerEmailSnapshot, r.PassengerPhoneSnapshot, r.PassengerCapturedAt, r.PassengerSnapshotSource }).ToListAsync();
            var orders = await db.PaymentOrders.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.ReservationId, p.PassengerId, p.Amount, p.Currency, p.PaymentStatus, p.ProviderReference, p.PaidAt, p.RefundStatus, p.RefundAmount, p.RefundKey, p.RefundReference, p.RefundedAt, p.RefundRequestedAt, p.RefundAttempts }).ToListAsync();
            var trips = await db.Trips.AsNoTracking().OrderBy(t => t.Id).Select(t => new { t.Id, t.BusId, t.RouteId, t.CompanyId, t.DepartureTime, t.ArrivalTime, t.Price, t.AvailableSeats, t.Status }).ToListAsync();
            var buses = await db.Buses.AsNoTracking().OrderBy(b => b.Id).Select(b => new { b.Id, b.CompanyId, b.SeatCapacity, b.PlateNumber, b.IsActive }).ToListAsync();
            return JsonSerializer.Serialize(new { users, reservations, orders, trips, buses });
        }
        var before = await ExistingData();
        await db.Database.MigrateAsync();
        if (before != await ExistingData()) throw new InvalidOperationException("Geçiş sonrası mevcut kayıtlar farklı; eş zamanlı işlemleri inceleyin.");
        if (await db.Trips.AnyAsync(t => t.SeatLayout != "Unspecified" || t.SeatPlanJson != null || t.SeatCapacitySnapshot != t.Bus.SeatCapacity || t.SellableSeatCount != t.Bus.SeatCapacity || t.DepartureTerminalId != null || t.ArrivalTerminalId != null) || await db.Terminals.AnyAsync())
            throw new InvalidOperationException("Eski seferlerin kapasite ve bilinmeyen yerleşim bilgileri doğrulanamadı.");
        Console.WriteLine("Koltuk ve terminal migration'ı uygulandı. Hesaplar, yolcu bilgileri, PNR, ödeme/iade kayıtları ve mevcut kontenjan korundu; eski seferlere tahmini plan veya terminal eklenmedi.");
    }
}
