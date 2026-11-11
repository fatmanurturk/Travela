using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Data;
using Travela.Tests;

internal static class SegmentMigrationChecks
{
    internal const string Previous = "20261110061925_AddPassengerGenderAndRouteGeometry";
    internal const string Migration = "20261111124242_AddIntermediateStopInventory";

    public static async Task Run(TravelaDbContext db, SqlTestDatabase target, Action<bool, string> check)
    {
        target.AssertTarget(db.Database.GetConnectionString());
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @company int,@bus int,@route int,@user int,@trip int,@active int,@cancelled int,@group uniqueidentifier=NEWID();
            INSERT Companies(Name,IsActive,CreatedAt) VALUES(N'Migration Firma',1,SYSUTCDATETIME()); SET @company=SCOPE_IDENTITY();
            INSERT Buses(CompanyId,PlateNumber,NormalizedPlateNumber,SeatCapacity,IsActive,CreatedAt) VALUES(@company,N'34 MIG 01',N'34MIG01',4,1,SYSUTCDATETIME()); SET @bus=SCOPE_IDENTITY();
            INSERT Routes(DepartureCity,ArrivalCity,EstimatedDurationMinutes,IsActive,CreatedAt) VALUES(N'İstanbul',N'Ankara',360,1,SYSUTCDATETIME()); SET @route=SCOPE_IDENTITY();
            INSERT Users(FullName,Email,PasswordHash,Role,IsActive,MustChangePassword,SecurityVersion,CreatedAt,TripRemindersEnabled,SmsNotificationsEnabled,CancellationNotificationsEnabled,RefundNotificationsEnabled)
            VALUES(N'Migration Yolcu',N'migration@test.invalid',N'unused',N'Passenger',1,0,1,SYSUTCDATETIME(),0,0,0,0); SET @user=SCOPE_IDENTITY();
            INSERT Trips(CompanyId,BusId,RouteId,DepartureTime,ArrivalTime,Price,AvailableSeats,Status,CreatedAt,ChangeVersion)
            VALUES(@company,@bus,@route,'2099-01-01T06:00:00','2099-01-01T12:00:00',500,2,N'Scheduled',SYSUTCDATETIME(),0); SET @trip=SCOPE_IDENTITY();
            INSERT Reservations(ReservationNumber,TripId,PassengerId,SeatNumber,Price,CreatedAt,Status,Kind)
            VALUES(REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),@trip,@user,1,500,SYSUTCDATETIME(),N'Active',N'Legacy');
            INSERT Reservations(ReservationNumber,TripId,PassengerId,SeatNumber,Price,CreatedAt,Status,Kind,Pnr,IssuedAt,PassengerGender,TicketJourneyJson)
            VALUES(REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),@trip,@user,2,500,SYSUTCDATETIME(),N'Active',N'Ticket',N'EXISTING-ACTIVE',SYSUTCDATETIME(),N'Female',N'{{"departureCity":"İstanbul","arrivalCity":"Ankara","departureTime":"2099-01-01T06:00:00Z","arrivalTime":"2099-01-01T12:00:00Z"}}'); SET @active=SCOPE_IDENTITY();
            INSERT Reservations(ReservationNumber,TripId,PassengerId,SeatNumber,Price,CreatedAt,Status,Kind,Pnr,IssuedAt,PassengerGender,CancelledAt,CancellationReason)
            VALUES(REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),@trip,@user,3,500,SYSUTCDATETIME(),N'Cancelled',N'Ticket',N'EXISTING-CANCELLED',SYSUTCDATETIME(),N'Male',SYSUTCDATETIME(),N'Geçmiş iptal'); SET @cancelled=SCOPE_IDENTITY();
            INSERT PurchaseGroups(Id,PassengerId,TripId,RequestKey,RequestHash,Amount,OriginalAmount,DiscountAmount,Currency,Provider,IsTest,PaymentStatus,CheckoutState,CreatedAt,HoldExpiresAt,ReconciliationAttempts,NeedsAttention)
            VALUES(@group,@user,@trip,NEWID(),REPLICATE('A',64),1000,1000,0,N'TRY',N'LocalTest',1,N'Paid',N'Ready',SYSUTCDATETIME(),DATEADD(minute,10,SYSUTCDATETIME()),0,0);
            INSERT PaymentOrders(Id,PurchaseGroupId,ReservationId,PassengerId,RequestKey,Amount,OriginalAmount,DiscountAmount,Currency,Provider,IsTest,PaymentStatus,ProviderReference,CreatedAt,PaidAt,RefundStatus,RefundAmount,RefundKey,RefundAttempts,NeedsAttention)
            VALUES(NEWID(),@group,@active,@user,NEWID(),500,500,0,N'TRY',N'LocalTest',1,N'Paid',N'existing-paid',SYSUTCDATETIME(),SYSUTCDATETIME(),N'None',0,REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),0,0);
            INSERT PaymentOrders(Id,PurchaseGroupId,ReservationId,PassengerId,RequestKey,Amount,OriginalAmount,DiscountAmount,Currency,Provider,IsTest,PaymentStatus,ProviderReference,CreatedAt,PaidAt,RefundStatus,RefundAmount,RefundKey,RefundAttempts,NeedsAttention,RefundReference,RefundedAt,RefundRequestedAt,RefundStatusChangedAt)
            VALUES(NEWID(),@group,@cancelled,@user,NEWID(),500,500,0,N'TRY',N'LocalTest',1,N'Paid',N'existing-refunded',SYSUTCDATETIME(),SYSUTCDATETIME(),N'Succeeded',500,REPLACE(CONVERT(nvarchar(36),NEWID()),'-',''),1,0,N'existing-refund',SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME());
            """);
        await db.Database.ExecuteSqlRawAsync("UPDATE Trips SET Price=0");
        try { await db.Database.MigrateAsync(); throw new Exception("Bozuk veri geçişi engellemedi"); }
        catch (SqlException error) when (error.Number == 51010) { check(true, "Bozuk eski veri geçişi durdurdu; migration uygulanmadı"); }
        check(await db.Trips.AnyAsync(t => t.Price == 0), "Geçiş bozuk fiyatı otomatik düzeltmedi");
        // Correct only the deliberately broken disposable fixture, as an operator would before retrying.
        await db.Database.ExecuteSqlRawAsync("UPDATE Trips SET Price=500");
        await using var sql = new SqlConnection(target.ConnectionString);
        await sql.OpenAsync();
        var snapshots = new List<(string Table, string Columns, string Order, long Count, string Hash)>();
        await using (var schema = sql.CreateCommand())
        {
            schema.CommandText = """
                SELECT t.name,
                  (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),',') WITHIN GROUP(ORDER BY c.column_id) FROM sys.columns c WHERE c.object_id=t.object_id),
                  (SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),',') WITHIN GROUP(ORDER BY ic.key_ordinal) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=t.object_id AND i.is_primary_key=1)
                FROM sys.tables t WHERE t.name<>N'__EFMigrationsHistory' ORDER BY t.name;
                """;
            await using var reader = await schema.ExecuteReaderAsync();
            while (await reader.ReadAsync()) snapshots.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), 0, ""));
        }
        async Task<(long Count, string Hash)> Fingerprint(string table, string columns, string order)
        {
            await using var command = sql.CreateCommand();
            // Identifiers come exclusively from SQL Server metadata, never from a request.
            command.CommandText = $"SELECT COUNT_BIG(*), (SELECT {columns} FROM [{table.Replace("]", "]]" )}] ORDER BY {order} FOR JSON PATH, INCLUDE_NULL_VALUES) FROM [{table.Replace("]", "]]" )}]";
            await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
            return (reader.GetInt64(0), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reader.IsDBNull(1) ? "[]" : reader.GetString(1)))));
        }
        for (var i = 0; i < snapshots.Count; i++)
        {
            var s = snapshots[i]; var f = await Fingerprint(s.Table, s.Columns, s.Order);
            snapshots[i] = s with { Count = f.Count, Hash = f.Hash };
        }
        check((await db.Database.GetPendingMigrationsAsync()).SequenceEqual([Migration]), "Geçiş yalnızca beklenen segment migration'ını içeriyor");
        await db.Database.MigrateAsync();
        foreach (var s in snapshots)
        {
            var after = await Fingerprint(s.Table, s.Columns, s.Order);
            check(after.Count == s.Count && after.Hash == s.Hash, $"Geçiş {s.Table}: {s.Count} kayıt ve eski sütun parmak izi korundu");
            Console.WriteLine($"PARMAKİZİ {s.Table}: önce={s.Hash}; sonra={after.Hash}");
        }
        check(await db.Reservations.CountAsync(r => r.BoardingStopOrder == 1 && r.AlightingStopOrder == 2) == 3, "İptal ve Legacy dahil bütün eski rezervasyonlar ilk→son durağa bağlandı");
        check(await db.ReservationLegs.CountAsync(l => l.IsActive) == 2 && await db.ReservationLegs.CountAsync() == 3, "İptal bacakları geçmiş olarak pasif kaldı");
        check(await db.PaymentOrders.SumAsync(p => p.Amount) == 1000 && await db.PaymentOrders.SumAsync(p => p.RefundAmount) == 500, "Finansal toplamlar: 1000 TL ödeme, 500 TL iade korundu");
        check(await db.PurchaseGroups.AnyAsync(g => g.BoardingStopOrder == 1 && g.AlightingStopOrder == 2 && g.RequestHash == new string('A',64)), "Eski ortak sipariş tam güzergâha bağlandı; idempotency özeti değiştirilmedi");
        try { await db.GetService<IMigrator>().MigrateAsync(Previous); throw new Exception("Geri dönüş engellenmedi"); }
        catch (SqlException error) when (error.Number == 51013) { check(true, "Veri silen Down reddedildi; yedekten dönüş gerektiriyor"); }
    }
}
