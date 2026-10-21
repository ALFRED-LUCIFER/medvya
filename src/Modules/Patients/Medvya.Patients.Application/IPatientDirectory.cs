namespace Medvya.Patients.Application;

/// <summary>What the schedule shows about a patient: name and MRN. Never identifiers, contact details or clinical data.</summary>
public sealed record PatientSummary(Guid Id, Guid FacilityId, string FullName, string Mrn);

/// <summary>
/// Patient summaries for other modules (the schedule, booking). Not audited here: the calling endpoint writes one audit event
/// per request before it runs (fail closed), so a page of appointments is one audit row, not one per patient.
/// </summary>
public interface IPatientDirectory
{
    /// <summary>Summaries of the patients of the current tenant among these ids; unknown ids are left out.</summary>
    Task<IReadOnlyDictionary<Guid, PatientSummary>> SummariesAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken = default);
}
