using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Data;

internal static class AccountStateMigration
{
    public static async Task Apply(TravelaDbContext db, bool email = false)
    {
        var migration = email ? "20261104071454_AddAccountEmailOutbox" : "20261104064425_SeparateAccountAccessStates";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0)
        {
            Console.WriteLine("İstenen migration zaten uygulanmış; veri değiştirilmedi.");
            return;
        }
        if (!pending.SequenceEqual(new[] { migration }))
            throw new InvalidOperationException("Bekleyen migration listesi beklenenden farklı; başka migration otomatik uygulanmadı.");

        async Task<string> Accounts()
        {
            var accounts = JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(u => u.Id)
                .Select(u => new { u.Id, u.Email, u.FullName, u.Role, u.CompanyId, u.IsActive, u.MustChangePassword, u.SecurityVersion,
                    u.PasswordHash, u.PasswordResetTokenHash, u.PasswordResetTokenExpiresAt, u.PasswordResetTokenUsedAt }).ToListAsync());
            return !email ? accounts : accounts + JsonSerializer.Serialize(await db.Users.AsNoTracking().OrderBy(u => u.Id)
                .Select(u => new { u.Id, u.ActivationPending, u.PasswordResetRequired }).ToListAsync());
        }
        async Task<string> Counts() => JsonSerializer.Serialize(new { Users = await db.Users.CountAsync(), Companies = await db.Companies.CountAsync(),
            Trips = await db.Trips.CountAsync(), Reservations = await db.Reservations.CountAsync(), Payments = await db.PaymentOrders.CountAsync() });
        var accountsBefore = await Accounts();
        var countsBefore = await Counts();
        await db.Database.MigrateAsync();
        if (accountsBefore != await Accounts() || countsBefore != await Counts())
            throw new InvalidOperationException("Migration sonrası veri karşılaştırması farklı; eş zamanlı uygulama işlemlerini ve kayıtları inceleyin.");
        if ((!email && await db.Users.AnyAsync(u => u.ActivationPending || u.PasswordResetRequired)) || (await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Migration sonrası hesap durumu doğrulaması başarısız.");
        Console.WriteLine("Migration uygulandı. Mevcut hesap izinleri, şifreler, token'lar, güvenlik sürümleri ve kayıt sayıları korundu; hiçbir pasif hesap açılmadı.");
    }
}
