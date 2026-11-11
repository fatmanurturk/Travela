using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Trips;
using Travela.Data;
using Travela.Models;
using Travela.Tests;

internal static class SegmentInventoryChecks
{
    public static async Task Run()
    {
        using var source = new TravelaDbContextFactory().CreateDbContext([]);
        var target = SqlTestDatabase.Create(Environment.GetEnvironmentVariable("TRAVELA_TEST_SQL_CONNECTION") ?? source.Database.GetConnectionString(), "TravelaPaymentTests_");
        await target.VerifyServerAsync();
        var options = new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(target.ConnectionString).Options;
        await using var db = new TravelaDbContext(options);
        var clock = new SegmentClock(); var enabled = Options.Create(new TripOptions { IntermediateStopsEnabled = true });
        var passed = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("BAŞARISIZ: " + message); passed++; Console.WriteLine("OK: " + message); }
        async Task Reject(Func<Task> action, string message)
        {
            try { await action(); }
            catch (PaymentFlowException) { Check(true, message); return; }
            catch (Exception e) when (SqlNumber(e) is 2601 or 2627 or 547 or 51011 or 51012 or 51013) { Check(true, message); return; }
            throw new Exception("BAŞARISIZ: " + message);
        }
        try
        {
            await SegmentMigrationChecks.Run(db, target, Check);
            db.ChangeTracker.Clear();
            var user = new User { FullName = "Segment Yolcusu", Email = "segment@example.test" };
            var company = new Company { Name = "Segment Firma" };
            var terminals = new[] { "İstanbul", "Bolu", "Ankara" }.Select(city => new Terminal { City = city, Name = city + " terminali", Address = city + " adresi" }).ToArray();
            db.AddRange(user, company); db.AddRange(terminals); await db.SaveChangesAsync();

            async Task<int> NewTrip()
            {
                var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara", DistanceKm = 400 };
                var bus = new Bus { CompanyId = company.Id, PlateNumber = Guid.NewGuid().ToString("N")[..12], SeatCapacity = 6, SeatLayout = "2+1" };
                bus.NormalizedPlateNumber = bus.PlateNumber;
                var trip = new Trip { CompanyId = company.Id, Bus = bus, Route = route, Price = 500, AvailableSeats = 6,
                    SeatLayout = "2+1", SeatCapacitySnapshot = 6, SellableSeatCount = 6, SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+1", 6)),
                    DepartureTime = clock.GetUtcNow().UtcDateTime.AddDays(3), ArrivalTime = clock.GetUtcNow().UtcDateTime.AddDays(3).AddHours(6) };
                trip.Stops = terminals.Select((t, i) => new TripStop { Trip = trip, StopOrder = i + 1, City = t.City, TerminalId = t.Id,
                    TerminalName = t.Name, TerminalAddress = t.Address, ArrivalTime = trip.DepartureTime.AddHours(3 * i),
                    DepartureTime = trip.DepartureTime.AddHours(3 * i), DistanceFromStartKm = 200 * i }).ToList();
                trip.Legs = [new() { Trip = trip, LegNumber = 1, FromStopOrder = 1, ToStopOrder = 2 }, new() { Trip = trip, LegNumber = 2, FromStopOrder = 2, ToStopOrder = 3 }];
                trip.SegmentFares = [new() { Trip = trip, FromStopOrder = 1, ToStopOrder = 3, Amount = 500, Source = "FullRoute", IsApproved = true, ApprovedAt = clock.GetUtcNow().UtcDateTime },
                    new() { Trip = trip, FromStopOrder = 1, ToStopOrder = 2, Amount = 250, IsApproved = true, ApprovedAt = clock.GetUtcNow().UtcDateTime },
                    new() { Trip = trip, FromStopOrder = 2, ToStopOrder = 3, Amount = 250, IsApproved = true, ApprovedAt = clock.GetUtcNow().UtcDateTime }];
                db.Trips.Add(trip); await db.SaveChangesAsync(); return trip.Id;
            }

            async Task<int> Hold(int tripId, int seat, int from, int to, string gender = "Female")
            {
                await using var context = new TravelaDbContext(options);
                await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var service = new TripSegmentService(context, clock, enabled);
                var trip = await service.LockTrip(tripId);
                var reservation = await service.HoldLocked(trip, user.Id, from, to, new(seat, gender));
                await service.SaveInventory(trip); await tx.CommitAsync(); return reservation.Id;
            }

            var id = await NewTrip();
            var a = await Hold(id, 3, 1, 2); var b = await Hold(id, 3, 2, 3, "Male");
            Check(await db.ReservationLegs.CountAsync(l => l.TripId == id && l.IsActive) == 2, "Aynı koltuk ardışık iki segmentte satılabilir");
            await Reject(async () => { await Hold(id, 3, 1, 3); }, "Çakışan tam güzergâh tutması reddedildi");
            var service = new TripSegmentService(db, clock, enabled);
            var noFlag = new TripSegmentService(db, clock);
            var loaded = await db.Trips.Include(t => t.Bus).SingleAsync(t => t.Id == id);
            await Reject(async () => { await noFlag.Resolve(loaded, 1, 2); }, "Bayrak varsayılan kapalı; segment satışı reddedildi");
            Check((await noFlag.Resolve(loaded)).Price == 500, "Parametresiz tam güzergâh fiyatı Trip.Price");
            await Reject(async () => { await service.Resolve(loaded, 2, 1); }, "Ters yön reddedildi");
            await Reject(async () => { await service.Resolve(loaded, 1, null); }, "Tek taraflı durak parametresi reddedildi");

            var concurrent = await NewTrip();
            async Task<bool> TryHold(int seat, int from, int to)
            {
                try { await Hold(concurrent, seat, from, to); return true; }
                catch (PaymentFlowException) { return false; }
                catch (Exception e) when (SqlNumber(e) is 2601 or 2627 or 1205) { return false; }
            }
            var overlap = await Task.WhenAll(TryHold(3, 1, 3), TryHold(3, 2, 3));
            Check(overlap.Count(x => x) == 1, "Gerçek SQL eşzamanlı çakışmada yalnızca bir tutma başarılı");
            var disjoint = await Task.WhenAll(TryHold(6, 1, 2), TryHold(6, 2, 3));
            Check(disjoint.All(x => x), "Gerçek SQL eşzamanlı çakışmayan segmentlerin ikisi de başarılı");

            var raw = await NewTrip();
            async Task RawInsert(int from, int to)
            {
                var number = Guid.NewGuid().ToString("N");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT Reservations (ReservationNumber,TripId,PassengerId,SeatNumber,Price,CreatedAt,Status,Kind,PassengerGender,BoardingStopOrder,AlightingStopOrder,BoardingDepartureSnapshot,AlightingArrivalSnapshot) SELECT {number},{raw},{user.Id},3,250,SYSUTCDATETIME(),N'Active',N'Legacy',N'Female',{from},{to},a.DepartureTime,b.ArrivalTime FROM TripStops a JOIN TripStops b ON b.TripId=a.TripId WHERE a.TripId={raw} AND a.StopOrder={from} AND b.StopOrder={to}");
            }
            await RawInsert(1, 2); await RawInsert(2, 3);
            Check(await db.ReservationLegs.CountAsync(l => l.TripId == raw && l.IsActive) == 2, "Doğrudan SQL de ardışık satış için eksiksiz bacak üretir");
            await Reject(() => RawInsert(1, 3), "Uygulama kilidi olmadan doğrudan SQL çifte satışı tekil indeksle engellendi");
            Check(await db.Reservations.CountAsync(r => r.TripId == raw) == 2, "Çakışan SQL statement ebeveyn ve bacakları birlikte geri aldı");

            var counter = await NewTrip(); await Hold(counter, 1, 1, 2); await Hold(counter, 4, 2, 3);
            Check(await db.Trips.Where(t => t.Id == counter).Select(t => t.AvailableSeats).SingleAsync() == 4, "Tam güzergâh boşluğu 4; bacak minimumu olan 5 kullanılmadı");
            await db.Trips.Where(t => t.Id == counter).ExecuteUpdateAsync(s => s.SetProperty(t => t.AvailableSeats, 0));
            await Hold(counter, 3, 1, 3);
            Check(await db.Trips.Where(t => t.Id == counter).Select(t => t.AvailableSeats).SingleAsync() == 3, "Bozuk önbellek satış kararı olmadı; gerçek bacaklardan yeniden üretildi");

            var boarding = await NewTrip();
            var now = clock.GetUtcNow().UtcDateTime;
            await db.Trips.Where(t => t.Id == boarding).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "InProgress").SetProperty(t => t.DepartureTime, now.AddHours(-1)).SetProperty(t => t.ArrivalTime, now.AddHours(1)));
            await db.TripStops.Where(s => s.TripId == boarding && s.StopOrder == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.ArrivalTime, now.AddHours(-1)).SetProperty(t => t.DepartureTime, now.AddHours(-1)));
            await db.TripStops.Where(s => s.TripId == boarding && s.StopOrder == 2).ExecuteUpdateAsync(s => s.SetProperty(t => t.ArrivalTime, now.AddMinutes(5)).SetProperty(t => t.DepartureTime, now.AddMinutes(5)));
            await db.TripStops.Where(s => s.TripId == boarding && s.StopOrder == 3).ExecuteUpdateAsync(s => s.SetProperty(t => t.ArrivalTime, now.AddHours(1)).SetProperty(t => t.DepartureTime, now.AddHours(1)));
            var shortHold = await Hold(boarding, 3, 2, 3);
            Check(await db.Reservations.AnyAsync(r => r.Id == shortHold && r.HoldExpiresAt == now.AddMinutes(5)), "İlk kalkış geçmiş olsa da ileri durak satılabilir; hold biniş kalkışını aşmaz");
            await Reject(async () => { await Hold(boarding, 6, 1, 3); }, "Biniş kalkışı geçmiş segment yeni satışa kapalı");

            var mixedGroupTrip = await NewTrip();
            await Reject(async () =>
            {
                await using var context = new TravelaDbContext(options);
                await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var group = new PurchaseGroup { TripId = mixedGroupTrip, PassengerId = user.Id, RequestKey = Guid.NewGuid(),
                    RequestHash = new string('A',64), BoardingStopOrder = 1, AlightingStopOrder = 3,
                    Amount = 250, OriginalAmount = 250, HoldExpiresAt = now.AddMinutes(10) };
                var fromStop = await context.TripStops.SingleAsync(s => s.TripId == mixedGroupTrip && s.StopOrder == 1);
                var toStop = await context.TripStops.SingleAsync(s => s.TripId == mixedGroupTrip && s.StopOrder == 2);
                var reservation = new Reservation { TripId = mixedGroupTrip, PassengerId = user.Id, SeatNumber = 3,
                    Kind = "Hold", Price = 250, PassengerGender = "Female", HoldExpiresAt = group.HoldExpiresAt };
                TripSegmentService.Capture(reservation, new(fromStop, toStop, 250, true));
                context.PaymentOrders.Add(new() { PurchaseGroup = group, Reservation = reservation, PassengerId = user.Id,
                    RequestKey = Guid.NewGuid(), Amount = 250, OriginalAmount = 250 });
                await context.SaveChangesAsync(); await tx.CommitAsync();
            }, "Grup ve yolcu farklı segmentteyse veritabanı işlemi reddeder");

            var genderTrip = await NewTrip(); await Hold(genderTrip, 1, 1, 2); await Hold(genderTrip, 1, 2, 3, "Male");
            await Reject(async () => { await Hold(genderTrip, 2, 1, 3); }, "Komşu cinsiyeti sonraki bacakta farklıysa tam güzergâh seçimi reddedildi");

            await Reject(async () => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ReservationLegs SET IsActive=0 WHERE ReservationId={a}"); }, "Doğrudan SQL aktif bacağı serbest bırakamaz");
            await Reject(async () => { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE ReservationLegs WHERE ReservationId={a}"); }, "Doğrudan SQL bacak geçmişini silemez");
            await Reject(async () => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Reservations SET AlightingStopOrder=3 WHERE Id={a}"); }, "Doğrudan SQL rezervasyon segmentini değiştiremez");
            await Reject(async () => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TripStops SET City=N'İzmir' WHERE TripId={id} AND StopOrder=2"); }, "Rezervasyon geçmişi olan seferin durak zinciri kilitli");

            await db.Reservations.Where(r => r.Id == a).ExecuteUpdateAsync(s => s.SetProperty(r => r.Kind, "Ticket").SetProperty(r => r.Pnr, "SEGMENT-TICKET").SetProperty(r => r.IssuedAt, clock.GetUtcNow().UtcDateTime));
            Check(await db.ReservationLegs.AnyAsync(l => l.ReservationId == a && l.IsActive), "Hold→Ticket geçişinde bacak bir an bile serbest kalmadı");
            await Reject(async () => { await Hold(id, 3, 1, 2); }, "Bilet kesildikten sonra aynı bacak satılamaz");
            await db.Reservations.Where(r => r.Id == a).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "Cancelled").SetProperty(r => r.CancelledAt, clock.GetUtcNow().UtcDateTime));
            Check(await db.ReservationLegs.AnyAsync(l => l.ReservationId == a && !l.IsActive), "İptal bacağı pasifleştirdi ve geçmişi korudu");
            await Reject(async () => { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE ReservationLegs WHERE ReservationId={a}"); }, "Pasif bacak geçmişi de doğrudan SQL ile silinemez");
            await Hold(id, 3, 1, 2);
            clock.Utc = clock.Utc.AddMinutes(11);
            await Hold(id, 3, 2, 3);
            Check(await db.ReservationLegs.AnyAsync(l => l.ReservationId == b && !l.IsActive), "Süre aşımı eski bacağı pasifleştirdi; sonraki tutma başarılı");

            var priceTrip = await NewTrip(); var price = await db.Trips.Include(t => t.Bus).SingleAsync(t => t.Id == priceTrip);
            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var manual = await db.TripSegmentFares.SingleAsync(f => f.TripId == priceTrip && f.FromStopOrder == 1 && f.ToStopOrder == 2);
                manual.Amount = 123.45m;
                var automatic = await db.TripSegmentFares.SingleAsync(f => f.TripId == priceTrip && f.FromStopOrder == 2 && f.ToStopOrder == 3);
                automatic.Source = "Automatic"; automatic.Amount = 1;
                await db.SaveChangesAsync(); await service.RecalculateAutomaticFares(price); await db.SaveChangesAsync(); await tx.CommitAsync();
            }
            Check((await db.TripSegmentFares.SingleAsync(f => f.TripId == priceTrip && f.FromStopOrder == 1 && f.ToStopOrder == 2)).Amount == 123.45m, "Manuel fiyat otomatik hesaplamayla ezilmedi");
            Check(!(await service.Resolve(price, 2, 3)).IsApproved, "Yeni otomatik fiyat firma onayı bekliyor");
            await Reject(async () => { await Hold(priceTrip, 3, 2, 3); }, "Firma onayı olmayan otomatik fiyatla satış engellendi");
            var stops = await db.TripStops.Where(s => s.TripId == priceTrip).OrderBy(s => s.StopOrder).ToListAsync();
            Check(TripSegmentService.AutomaticFare(1.01m, stops[0], stops[2], stops[0], stops[1]) == .51m, "Mesafe oranı ve AwayFromZero yuvarlaması");
            stops[1].DistanceFromStartKm = null;
            Check(TripSegmentService.AutomaticFare(1.01m, stops[0], stops[2], stops[0], stops[1]) == .51m, "Mesafe yoksa süre oranı kullanılır");
            Check(TripSegmentService.AutomaticFare(.01m, stops[0], stops[2], stops[0], stops[1]) == .01m, "Otomatik fiyat en az 0,01 TL");
            Console.WriteLine($"SONUÇ: {passed} segment model/migration/SQL eşzamanlılık kontrolü başarılı.");
        }
        finally { await target.DeleteAsync(db); }
    }

    private static int? SqlNumber(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException) if (current is SqlException sql) return sql.Number;
        return null;
    }

    private sealed class SegmentClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
