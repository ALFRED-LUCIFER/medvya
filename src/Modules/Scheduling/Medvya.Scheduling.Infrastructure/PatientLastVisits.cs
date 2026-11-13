using Medvya.Patients.Application;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>When each patient last attended, for the patient search row (MDV-P1-101): the start time only, within the tenant (EF filter and row-level security).</summary>
public sealed class PatientLastVisits(SchedulingDbContext db) : IPatientLastVisits
{
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastVisitsAsync(IReadOnlyCollection<Guid> patientIds, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await db.Appointments.AsNoTracking().Where(a => patientIds.Contains(a.PatientId) && BookingService.Attended.Contains(a.State) && a.Start <= now)
            .GroupBy(a => a.PatientId).Select(g => new { g.Key, Last = g.Max(a => a.Start) }).ToDictionaryAsync(g => g.Key, g => g.Last, cancellationToken);
}
