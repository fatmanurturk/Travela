using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

namespace Travela.Api.Trips;

public record CrewConflict(int StaffId, string FullName, string Duty, int TripId, string DepartureCity, string ArrivalCity,
    DateTime DepartureTime, DateTime ArrivalTime, int? BatchIndex = null);
public sealed class CrewValidationException(string message) : Exception(message);
public sealed class CrewConflictException(List<CrewConflict> conflicts) : Exception("Seçtiğiniz personelin başka bir seferde görevi var. Sefer saatleri çakıştığı için atama yapılamaz. Lütfen farklı personel seçin veya sefer saatlerini düzenleyin.")
{
    public List<CrewConflict> Conflicts { get; } = conflicts;
}

public class TripCrewService(TravelaDbContext db)
{
    private static readonly string[] ActiveStatuses = ["Scheduled", "Planlandı", "Planlandi", "InProgress", "In Progress", "Devam Ediyor"];

    public async Task Prepare(Trip trip, CrewSelectionDto? selection,
        IReadOnlyList<Trip>? pending = null, CancellationToken ct = default)
    {
        var existing = trip.Id == 0 ? [] : await db.TripCrews.Where(c => c.TripId == trip.Id).ToListAsync(ct);
        var requested = selection == null ? existing.Select(c => (c.StaffId, c.Duty)).ToList()
            : selection.Drivers.Select(id => (StaffId: id, Duty: "Driver")).Concat(selection.Attendants.Select(id => (StaffId: id, Duty: "Attendant"))).ToList();
        if (requested.Count > 60 || requested.Any(c => c.StaffId <= 0) || requested.Select(c => c.StaffId).Distinct().Count() != requested.Count)
            throw new CrewValidationException("Aynı personel bir sefere yalnızca bir kez atanabilir. Geçerli personel seçin.");
        if (existing.Any(c => c.CompanyId != trip.CompanyId)) throw new CrewValidationException("Ekip ataması olan sefer başka firmaya aktarılamaz. Önce ekip atamalarını kaldırın.");
        // Serialize assignments for each staff member, including simultaneous edits on different trips.
        var people = new Dictionary<int, CompanyStaff>();
        foreach (var id in requested.Select(c => c.StaffId).Order()) {
            var staff = await db.CompanyStaff.FromSqlInterpolated($"SELECT * FROM CompanyStaff WITH (UPDLOCK,HOLDLOCK) WHERE Id={id} AND CompanyId={trip.CompanyId}").SingleOrDefaultAsync(ct);
            if (staff == null) throw new CrewValidationException("Personel bulunamadı veya firmanıza ait değil.");
            var duty = requested.Single(c => c.StaffId == id).Duty;
            var retained = existing.Any(c => c.StaffId == id && c.Duty == duty);
            if (!retained && (!staff.IsActive || staff.Duty != duty)) throw new CrewValidationException("Yeni atamalarda yalnızca görevine uygun aktif personel seçilebilir.");
            people[id] = staff;
        }
        var ids = requested.Select(c => c.StaffId).ToList();
        var conflicts = await db.TripCrews.AsNoTracking().Where(c => ids.Contains(c.StaffId) && c.CompanyId == trip.CompanyId && c.TripId != trip.Id &&
            ActiveStatuses.Contains(c.Trip.Status) && c.Trip.DepartureTime < trip.ArrivalTime && c.Trip.ArrivalTime > trip.DepartureTime)
            .Select(c => new CrewConflict(c.StaffId, c.Staff.FullName, c.Duty, c.TripId, c.Trip.Route.DepartureCity, c.Trip.Route.ArrivalCity,
                DateTime.SpecifyKind(c.Trip.DepartureTime, DateTimeKind.Utc), DateTime.SpecifyKind(c.Trip.ArrivalTime, DateTimeKind.Utc), null)).ToListAsync(ct);
        if (pending != null) foreach (var (other, index) in pending.Select((t, i) => (t, i))) {
            if (other.CompanyId != trip.CompanyId || other.DepartureTime >= trip.ArrivalTime || other.ArrivalTime <= trip.DepartureTime) continue;
            foreach (var member in db.TripCrews.Local.Where(c => ReferenceEquals(c.Trip, other) && ids.Contains(c.StaffId)))
                conflicts.Add(new(member.StaffId, people[member.StaffId].FullName, member.Duty, 0, other.Route.DepartureCity, other.Route.ArrivalCity,
                    DateTime.SpecifyKind(other.DepartureTime, DateTimeKind.Utc), DateTime.SpecifyKind(other.ArrivalTime, DateTimeKind.Utc), index + 1));
        }
        conflicts = conflicts.OrderBy(c => c.StaffId).ThenBy(c => c.TripId).ThenBy(c => c.BatchIndex).ToList();
        if (conflicts.Count > 0) throw new CrewConflictException(conflicts);
        foreach (var member in existing.Where(c => !requested.Any(r => r.StaffId == c.StaffId && r.Duty == c.Duty))) db.TripCrews.Remove(member);
        foreach (var member in requested.Where(r => !existing.Any(c => c.StaffId == r.StaffId && c.Duty == r.Duty))) {
            var replaced = existing.SingleOrDefault(c => c.StaffId == member.StaffId);
            if (replaced != null) { db.Entry(replaced).State = EntityState.Modified; replaced.Duty = member.Duty; }
            else db.TripCrews.Add(new TripCrew { Trip = trip, TripId = trip.Id, CompanyId = trip.CompanyId, StaffId = member.StaffId, Duty = member.Duty });
        }
    }
}
