using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>A medicine prescribed at a visit, with its dose as written (blank when none was).</summary>
public sealed record VisitMedicine(string Name, string? Dose);

/// <summary>A diagnosis recorded at a visit; the code stays blank when the doctor did not code it.</summary>
public sealed record VisitDiagnosis(string Description, string? Code);

/// <summary>One past visit on the chart (MDV-1088). <see cref="NoteSigned"/>: the note is signed, so the visit opens read-only.</summary>
public sealed record VisitSummary(Guid EncounterId, DateTimeOffset StartedAt, EncounterStatus Status, Guid LeadClinicianId, bool NoteSigned,
    IReadOnlyList<VisitDiagnosis> Diagnoses, IReadOnlyList<VisitMedicine> Medicines, IReadOnlyList<string> Tests);

/// <summary>An investigation placed at one encounter. Implemented by Prescribing (it owns the orders); only placed orders, never drafts.</summary>
public sealed record PlacedTest(Guid EncounterId, string Name);

public interface IVisitTests
{
    Task<IReadOnlyList<PlacedTest>> ForEncountersAsync(IReadOnlyCollection<Guid> encounterIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// The patient's past visits with what was found and prescribed (MDV-1088), newest first. Same gate as the chart: clinical access first (role,
/// care relationship), then the read, then the audit event with the patient.
/// </summary>
public interface IPatientVisits
{
    /// <summary>The latest <see cref="Limit"/> visits; older ones are on the full timeline.</summary>
    Task<IReadOnlyList<VisitSummary>> ListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    public const int Limit = 30;
}
