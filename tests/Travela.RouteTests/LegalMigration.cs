using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Data;

internal static class LegalMigration
{
    public static async Task Apply(TravelaDbContext db)
    {
        const string target = "20261109131835_AddLegalAcceptances";
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length == 0)
        {
            Console.WriteLine("Hukuki onay migration'ı zaten uygulanmış.");
            return;
        }
        if (pending.Length != 1 || pending[0] != target)
            throw new Exception("Bekleyen değişiklikler yalnızca hukuki onay migration'ından oluşmuyor; uygulanmadı.");

        async Task<int[]> Counts() =>
        [
            await db.Users.CountAsync(),
            await db.Companies.CountAsync(),
            await db.Buses.CountAsync(),
            await db.Routes.CountAsync(),
            await db.Trips.CountAsync(),
            await db.Reservations.CountAsync(),
            await db.PaymentOrders.CountAsync(),
            await db.PurchaseGroups.CountAsync()
        ];
        var before = await Counts();
        await db.GetService<IMigrator>().MigrateAsync(target);
        var after = await Counts();
        if (!before.SequenceEqual(after))
            throw new Exception("Migration sırasında mevcut kayıt sayıları değişti; eşzamanlı işlemleri kontrol edin.");
        Console.WriteLine("Yalnızca LegalAcceptances tablosu, ilişkileri ve indeksleri eklendi.");
        Console.WriteLine("Mevcut kullanıcı/firma/otobüs/güzergâh/sefer/bilet/ödeme/sipariş sayıları korundu: " + string.Join(", ", after));
        Console.WriteLine("Geçmiş kayıtlar için geriye dönük onay oluşturulmadı: " + await db.LegalAcceptances.CountAsync());
    }
}
