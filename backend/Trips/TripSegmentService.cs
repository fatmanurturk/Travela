using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Routes;
using Travela.Api.Security;
using Travela.Data;

namespace Travela.Api.Trips;

public sealed class TripOptions
{
    public bool IntermediateStopsEnabled { get; set; }
}

public sealed record TripSegment(TripStop From, TripStop To, decimal Price, bool IsApproved)
{
    public int TripId => From.TripId;
}

public sealed class TripSegmentService(TravelaDbContext db, TimeProvider clock, IOptions<TripOptions>? options = null)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    public bool IntermediateStopsEnabled => options?.Value.IntermediateStopsEnabled == true;

    public static void InitializeFullJourney(Trip trip, Travela.Api.Models.Route route)
    {
        trip.Stops = [
            new() { Trip = trip, StopOrder = 1, City = route.DepartureCity, ArrivalTime = trip.DepartureTime,
                DepartureTime = trip.DepartureTime, TerminalId = trip.DepartureTerminalId, TerminalName = trip.DepartureTerminalName,
                TerminalAddress = trip.DepartureTerminalAddress, BoardingInstructions = trip.BoardingInstructions, DistanceFromStartKm = 0 },
            new() { Trip = trip, StopOrder = 2, City = route.ArrivalCity, ArrivalTime = trip.ArrivalTime,
                DepartureTime = trip.ArrivalTime, TerminalId = trip.ArrivalTerminalId, TerminalName = trip.ArrivalTerminalName,
                TerminalAddress = trip.ArrivalTerminalAddress, DistanceFromStartKm = route.DistanceKm }
        ];
        trip.Legs = [new() { Trip = trip, LegNumber = 1, FromStopOrder = 1, ToStopOrder = 2 }];
        trip.SegmentFares = [new() { Trip = trip, FromStopOrder = 1, ToStopOrder = 2,
            Amount = trip.Price, Source = "FullRoute", IsApproved = true, ApprovedAt = DateTime.UtcNow }];
    }

    public async Task<TripSegment> Resolve(Trip trip, int? from = null, int? to = null, CancellationToken ct = default)
    {
        if (from.HasValue != to.HasValue) throw new PaymentFlowException("Biniş ve iniş duraklarını birlikte seçiniz.", 400);
        var stops = await db.TripStops.AsNoTracking().Where(s => s.TripId == trip.Id).OrderBy(s => s.StopOrder).ToListAsync(ct);
        if (stops.Count < 2 || !stops.Select(s => s.StopOrder).SequenceEqual(Enumerable.Range(1, stops.Count)))
            throw new PaymentFlowException("Seferin durak zinciri doğrulanamadı.");
        var first = from ?? 1; var last = to ?? stops.Count;
        if (first < 1 || last > stops.Count || first >= last) throw new PaymentFlowException("Durak çifti geçersiz.", 400);
        var full = first == 1 && last == stops.Count;
        if (!full && !IntermediateStopsEnabled) throw new PaymentFlowException("Ara durak satışı henüz açık değil.", 400);
        var legCount = await db.TripLegs.CountAsync(l => l.TripId == trip.Id, ct);
        if (legCount != stops.Count - 1) throw new PaymentFlowException("Seferin bacak zinciri eksik.");
        if (full) return new(stops[first - 1], stops[last - 1], trip.Price, true);
        var fare = await db.TripSegmentFares.AsNoTracking().SingleOrDefaultAsync(f => f.TripId == trip.Id && f.FromStopOrder == first && f.ToStopOrder == last, ct);
        if (fare == null) throw new PaymentFlowException("Bu segment için fiyat tanımlanmadı.");
        return new(stops[first - 1], stops[last - 1], fare.Amount, fare.IsApproved);
    }

    public IQueryable<ReservationLeg> OccupiedLegs(int tripId, int from, int to) => db.ReservationLegs.AsNoTracking()
        .Where(l => l.TripId == tripId && l.LegNumber >= from && l.LegNumber < to && l.IsActive &&
            l.Reservation.Status == "Active" && (l.Reservation.Kind != "Hold" || l.Reservation.HoldExpiresAt > Now));

    public async Task<List<int>> AvailableSeatNumbers(Trip trip, int from, int to, CancellationToken ct = default)
    {
        if (from < 1 || to <= from || await db.TripStops.CountAsync(s => s.TripId == trip.Id && (s.StopOrder == from || s.StopOrder == to), ct) != 2)
            throw new PaymentFlowException("Durak çifti geçersiz.", 400);
        var capacity = trip.SeatCapacitySnapshot ?? (trip.Bus ?? await db.Buses.SingleAsync(b => b.Id == trip.BusId, ct)).SeatCapacity;
        List<SeatPosition> plan;
        try { plan = SeatPlans.Read(trip.SeatPlanJson, trip.SeatLayout, capacity); }
        catch (ArgumentException error) { throw new PaymentFlowException(error.Message); }
        var occupied = await OccupiedLegs(trip.Id, from, to).Select(l => l.SeatNumber).Distinct().ToListAsync(ct);
        return plan.Where(s => !s.IsBlocked && !occupied.Contains(s.Number)).Select(s => s.Number).ToList();
    }

    public async Task<string?> ValidateSelection(Trip trip, int from, int to, IReadOnlyList<SeatPassenger> passengers,
        CancellationToken ct = default, IReadOnlyList<int>? excluding = null)
    {
        List<SeatPosition> plan;
        try { plan = SeatPlans.ReadVerified(trip.SeatPlanJson, trip.SeatLayout, trip.SeatCapacitySnapshot ?? trip.Bus.SeatCapacity); }
        catch (ArgumentException error) { return error.Message; }
        var excluded = excluding ?? [];
        var occupied = await OccupiedLegs(trip.Id, from, to).Where(l => !excluded.Contains(l.ReservationId))
            .Select(l => new { l.LegNumber, l.SeatNumber, l.Reservation.PassengerGender, l.Reservation.Kind, l.Reservation.HoldExpiresAt }).ToListAsync(ct);
        for (var leg = from; leg < to; leg++)
        {
            var conflict = SeatCompatibility.Validate(plan, passengers, occupied.Where(l => l.LegNumber == leg)
                .Select(l => new SeatOccupancy(l.SeatNumber, l.PassengerGender, l.Kind, l.HoldExpiresAt)).ToList(), Now);
            if (conflict != null) return conflict;
        }
        return null;
    }

    public static void Capture(Reservation reservation, TripSegment segment)
    {
        reservation.BoardingStopOrder = segment.From.StopOrder;
        reservation.AlightingStopOrder = segment.To.StopOrder;
        reservation.BoardingDepartureSnapshot = segment.From.DepartureTime;
        reservation.AlightingArrivalSnapshot = segment.To.ArrivalTime;
    }

    public async Task<Trip> LockTrip(int tripId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Segment yazımı Serializable transaction gerektirir.");
        return await db.Trips.FromSqlInterpolated($"SELECT * FROM Trips WITH (UPDLOCK, HOLDLOCK) WHERE Id = {tripId}")
            .Include(t => t.Bus).Include(t => t.Route).Include(t => t.Company)
            .Include(t => t.DepartureTerminal).Include(t => t.ArrivalTerminal).SingleAsync(ct);
    }

    public async Task<Reservation> HoldLocked(Trip trip, int passengerId, int from, int to, SeatPassenger passenger, CancellationToken ct = default)
    {
        trip = await LockTrip(trip.Id, ct);
        await ExpireLocked(trip, ct);
        var segment = await Resolve(trip, from, to, ct);
        if (!segment.IsApproved || segment.From.DepartureTime <= Now ||
            trip.Status.ToLowerInvariant() is not ("scheduled" or "planlandı" or "planlandi" or "inprogress" or "in progress" or "devam ediyor") ||
            !trip.Company.IsActive || !trip.Route.IsActive || !trip.Bus.IsActive || trip.Bus.CompanyId != trip.CompanyId)
            throw new PaymentFlowException("Segment satışa açık değil veya fiyat firma onayı bekliyor.");
        if (!await db.Users.SessionAccounts(db).AnyAsync(u => u.Id == passengerId && u.Role == "Passenger", ct))
            throw new PaymentFlowException("Aktif bir yolcu hesabı gereklidir.", 401);
        var terminals = new[] { segment.From.TerminalId, segment.To.TerminalId }.Where(t => t.HasValue).Select(t => t!.Value).ToArray();
        if (await db.Terminals.AnyAsync(t => terminals.Contains(t.Id) && !t.IsActive, ct)) throw new PaymentFlowException("Segment terminali satışa kapalı.");
        var conflict = await ValidateSelection(trip, from, to, [passenger], ct);
        if (conflict != null) throw new PaymentFlowException(conflict);
        var hold = new Reservation { TripId = trip.Id, PassengerId = passengerId, SeatNumber = passenger.SeatNumber,
            PassengerGender = passenger.Gender, Kind = "Hold", Price = segment.Price,
            HoldExpiresAt = new[] { Now.AddMinutes(10), segment.From.DepartureTime }.Min() };
        Capture(hold, segment);
        db.Reservations.Add(hold);
        return hold;
    }

    public void Release(Reservation reservation, string reason)
    {
        if (reservation.Status != "Active") return;
        reservation.Status = "Cancelled"; reservation.CancelledAt = Now; reservation.CancellationReason = reason;
    }

    public async Task ExpireLocked(Trip trip, CancellationToken ct = default)
    {
        foreach (var hold in await db.Reservations.Where(r => r.TripId == trip.Id && r.Status == "Active" && r.Kind == "Hold" && r.HoldExpiresAt <= Now).ToListAsync(ct))
            Release(hold, "Ödeme süresi doldu. Bilet düzenlenmedi.");
        await SaveInventory(trip, ct);
    }

    public async Task SaveInventory(Trip trip, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Envanter ve önbellek aynı transaction içinde yazılmalıdır.");
        await db.SaveChangesAsync(ct);
        var last = await db.TripStops.Where(s => s.TripId == trip.Id).MaxAsync(s => s.StopOrder, ct);
        trip.AvailableSeats = (await AvailableSeatNumbers(trip, 1, last, ct)).Count;
        await db.SaveChangesAsync(ct);
    }

    public static decimal AutomaticFare(decimal fullPrice, TripStop first, TripStop last, TripStop from, TripStop to)
    {
        if (fullPrice < .01m) throw new ArgumentException("Tam güzergâh fiyatı pozitif olmalıdır.");
        decimal ratio;
        if (first.DistanceFromStartKm.HasValue && last.DistanceFromStartKm > first.DistanceFromStartKm &&
            from.DistanceFromStartKm >= first.DistanceFromStartKm && to.DistanceFromStartKm > from.DistanceFromStartKm && to.DistanceFromStartKm <= last.DistanceFromStartKm)
            ratio = (to.DistanceFromStartKm!.Value - from.DistanceFromStartKm!.Value) / (last.DistanceFromStartKm!.Value - first.DistanceFromStartKm.Value);
        else
        {
            var total = last.ArrivalTime - first.DepartureTime;
            var segment = to.ArrivalTime - from.DepartureTime;
            if (total <= TimeSpan.Zero || segment <= TimeSpan.Zero || segment > total) throw new ArgumentException("Fiyat oranı için mesafe veya süre geçersiz.");
            ratio = (decimal)segment.Ticks / total.Ticks;
        }
        return Math.Max(0.01m, decimal.Round(fullPrice * ratio, 2, MidpointRounding.AwayFromZero));
    }

    public async Task ValidateStops(IReadOnlyList<TripStop> stops, CancellationToken ct = default)
    {
        if (stops.Count is < 2 or > 81 || !stops.Select(s => s.StopOrder).SequenceEqual(Enumerable.Range(1, stops.Count)) ||
            stops.Any(s => TurkishCities.Normalize(s.City) != s.City) || stops.Select(s => s.City).Distinct().Count() != stops.Count)
            throw new ArgumentException("Duraklar sıralı, tekrarsız ve Türkiye şehirlerinden seçilmiş olmalıdır.");
        foreach (var (stop, index) in stops.Select((s, i) => (s, i)))
        {
            if (stop.ArrivalTime > stop.DepartureTime || index > 0 && stops[index - 1].DepartureTime >= stop.ArrivalTime ||
                stop.DistanceFromStartKm < 0 || index > 0 && stop.DistanceFromStartKm.HasValue && stops[index - 1].DistanceFromStartKm >= stop.DistanceFromStartKm)
                throw new ArgumentException("Durak saatleri ve mesafeleri ileri yönde olmalıdır.");
            if (!stop.TerminalId.HasValue || !await db.Terminals.AnyAsync(t => t.Id == stop.TerminalId && t.IsActive && t.City == stop.City, ct))
                throw new ArgumentException("Her durak için şehriyle eşleşen aktif terminal gereklidir.");
        }
    }

    public async Task RecalculateAutomaticFares(Trip trip, CancellationToken ct = default)
    {
        await LockTrip(trip.Id, ct);
        var stops = await db.TripStops.Where(s => s.TripId == trip.Id).OrderBy(s => s.StopOrder).ToListAsync(ct);
        await ValidateStops(stops, ct);
        var fares = await db.TripSegmentFares.Where(f => f.TripId == trip.Id).ToListAsync(ct);
        foreach (var from in stops) foreach (var to in stops.Where(s => s.StopOrder > from.StopOrder))
        {
            var full = from.StopOrder == 1 && to.StopOrder == stops.Count;
            var fare = fares.SingleOrDefault(f => f.FromStopOrder == from.StopOrder && f.ToStopOrder == to.StopOrder);
            if (!full && fare?.Source == "Manual") continue;
            var amount = full ? trip.Price : AutomaticFare(trip.Price, stops[0], stops[^1], from, to);
            if (fare == null)
            {
                fare = new() { TripId = trip.Id, FromStopOrder = from.StopOrder, ToStopOrder = to.StopOrder };
                db.TripSegmentFares.Add(fare);
            }
            else if (fare.Amount == amount && fare.Source == (full ? "FullRoute" : "Automatic")) continue;
            fare.Amount = amount; fare.Source = full ? "FullRoute" : "Automatic";
            fare.IsApproved = full; fare.ApprovedAt = full ? Now : null;
        }
    }

    public async Task SynchronizeFullJourney(Trip trip, CancellationToken ct = default)
    {
        var stops = await db.TripStops.Where(s => s.TripId == trip.Id).OrderBy(s => s.StopOrder).ToListAsync(ct);
        if (stops.Count != 2) throw new PaymentFlowException("Ara duraklı sefer bu eski düzenleme akışında değiştirilemez.");
        var first = stops[0]; var last = stops[1];
        first.City = trip.Route.DepartureCity; last.City = trip.Route.ArrivalCity;
        first.ArrivalTime = first.DepartureTime = trip.DepartureTime;
        last.ArrivalTime = last.DepartureTime = trip.ArrivalTime;
        first.TerminalId = trip.DepartureTerminalId; last.TerminalId = trip.ArrivalTerminalId;
        first.TerminalName = trip.DepartureTerminalName; last.TerminalName = trip.ArrivalTerminalName;
        first.TerminalAddress = trip.DepartureTerminalAddress; last.TerminalAddress = trip.ArrivalTerminalAddress;
        first.BoardingInstructions = trip.BoardingInstructions;
        var fare = await db.TripSegmentFares.SingleAsync(f => f.TripId == trip.Id && f.FromStopOrder == 1 && f.ToStopOrder == 2, ct);
        fare.Amount = trip.Price;
    }
}
