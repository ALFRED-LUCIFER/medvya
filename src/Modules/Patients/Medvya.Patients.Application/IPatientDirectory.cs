using Medvya.Patients.Domain;
namespace Medvya.Patients.Application;

/// <summary>
/// What the schedule and the reception board show about a patient: name and MRN, plus date of birth and whether registration
/// is incomplete to confirm identity at check-in, and the preferred language as recorded (the chart banner, MDV-P2-032). Never identifiers,
/// contact details or clinical data.
/// </summary>
public sealed record PatientSummary(Guid Id, Guid FacilityId, string FullName, string Mrn, DateOnly DateOfBirth, bool RegistrationIncomplete, SexAtBirth SexAtBirth, string? PreferredLanguage = null);

/// <summary>
/// Patient summaries for other modules (the schedule, booking). Not audited here: the calling endpoint writes one audit event
/// per request before it runs (fail closed), so a page of appointments is one audit row, not one per patient.
/// </summary>
public interface IPatientDirectory
{
    /// <summary>Summaries of the patients of the current tenant among these ids; unknown ids are left out.</summary>
    Task<IReadOnlyDictionary<Guid, PatientSummary>> SummariesAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken = default);
}
