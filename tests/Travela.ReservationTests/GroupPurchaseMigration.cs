using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Data;

internal static class GroupPurchaseMigration
{
    public static async Task Apply(TravelaDbContext db)
    {
        const string migration = "20261104135339_AddGroupPurchasesAndIyzicoSandbox";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0) { Console.WriteLine("Ortak sipariş migration'ı zaten uygulanmış; veri değiştirilmedi."); return; }
        if (!pending.SequenceEqual(new[] { migration })) throw new InvalidOperationException("Bekleyen migration listesi farklı; otomatik geçiş yapılmadı.");
        async Task<string> ExistingData()
        {
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.CompanyId, u.IsActive, u.SecurityVersion, u.ActivationPending, u.PasswordResetRequired, u.TripRemindersEnabled, u.CancellationNotificationsEnabled, u.RefundNotificationsEnabled, u.EmailNotificationsEnabled, u.SmsNotificationsEnabled }).ToListAsync();
            var reservations = await db.Reservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.PassengerId, r.TripId, r.SeatNumber, r.Price, r.Pnr, r.Kind, r.Status, r.CreatedAt, r.IssuedAt, r.HoldExpiresAt, r.CancelledAt, r.CancellationReason, r.PassengerNameSnapshot, r.PassengerEmailSnapshot, r.PassengerPhoneSnapshot, r.PassengerCapturedAt, r.PassengerSnapshotSource, r.TicketJourneyJson, r.ChangeCancellationUntil }).ToListAsync();
            var orders = await db.PaymentOrders.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.ReservationId, p.PassengerId, p.Amount, p.Currency, p.PaymentStatus, p.ProviderReference, p.PaidAt, p.RefundStatus, p.RefundAmount, p.RefundKey, p.RefundReference, p.RefundedAt, p.RefundRequestedAt, p.RefundAttempts, p.Provider, p.IsTest }).ToListAsync();
            var trips = await db.Trips.AsNoTracking().OrderBy(t => t.Id).Select(t => new { t.Id, t.BusId, t.RouteId, t.CompanyId, t.DepartureTime, t.ArrivalTime, t.Price, t.AvailableSeats, t.Status, t.ChangeVersion, t.SeatPlanJson, t.SeatLayout, t.SellableSeatCount, t.SeatCapacitySnapshot, t.DepartureTerminalId, t.ArrivalTerminalId, t.DepartureTerminalName, t.ArrivalTerminalName, t.DepartureTerminalAddress, t.ArrivalTerminalAddress, t.BoardingInstructions }).ToListAsync();
            var buses = await db.Buses.AsNoTracking().OrderBy(b => b.Id).Select(b => new { b.Id, b.CompanyId, b.SeatCapacity, b.PlateNumber, b.IsActive, b.SeatLayout, b.SeatPlanJson }).ToListAsync();
            return JsonSerializer.Serialize(new { users, reservations, orders, trips, buses, paymentEvents = await db.PaymentEvents.CountAsync(), notifications = await db.TravelNotifications.CountAsync(), changes = await db.TripChanges.CountAsync(), terminals = await db.Terminals.CountAsync() });
        }
        var before = await ExistingData();
        await db.Database.MigrateAsync();
        if (before != await ExistingData()) throw new InvalidOperationException("Geçiş sonrası mevcut kayıtlar farklı; eş zamanlı işlemleri inceleyin.");
        if (await db.PurchaseGroups.AnyAsync() || await db.ProviderRefundAttempts.AnyAsync() || await db.PaymentOrders.AnyAsync(p => p.PurchaseGroupId != null))
            throw new InvalidOperationException("Eski işlemler ortak siparişe dönüştürülmediği için yeni tablolar boş olmalı.");
        Console.WriteLine("Ortak sipariş ve iyzico sandbox migration'ı uygulandı. Hesaplar, yolcu/yolculuk bilgileri, PNR, ödeme/iade, bildirim/değişiklik geçmişi ve mevcut kontenjan korundu.");
    }
}