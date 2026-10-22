using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Infrastructure;

/// <summary>Reads only unencrypted demographics, so it needs no column master key. Tenant filters and RLS apply.</summary>
public sealed class PatientDirectory(PatientsDbContext db) : IPatientDirectory
{
    public async Task<IReadOnlyDictionary<Guid, PatientSummary>> SummariesAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patientIds);
        if (patientIds.Count == 0) return new Dictionary<Guid, PatientSummary>();
        var ids = patientIds.Distinct().ToList();
        return await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new PatientSummary(p.Id, p.FacilityId, p.FullName, p.Mrn))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }
}
