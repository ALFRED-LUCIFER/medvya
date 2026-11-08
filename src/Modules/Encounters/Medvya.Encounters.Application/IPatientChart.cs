using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>Allergy state for the banner: three different things, never merged. Listed also covers "unknown" entries (they are shown, not hidden).</summary>
public enum AllergyState { NotRecorded, NoKnownAllergies, Listed }

public sealed record AllergyView(string Substance, string? Reaction, AllergyStatus Status, ChartSource Source);
public sealed record ProblemView(string? Icd10Code, string Description, ProblemVerification Verification, DateOnly? OnsetOn, ChartSource Source);
public sealed record MedicineView(string Name, string? Dose, MedicationState State, ChartSource Source);
public sealed record VitalView(ObservationCode Code, decimal Value, string Unit, DateTimeOffset TakenAt, ChartSource Source);
public sealed record TrendPoint(decimal Value, DateTimeOffset TakenAt, ChartSource Source);
/// <summary>The last readings of one measure, oldest first. Blood pressure is two series (systolic and diastolic).</summary>
public sealed record TrendView(ObservationCode Code, string Unit, IReadOnlyList<TrendPoint> Points);
public sealed record RecentEncounter(Guid Id, DateTimeOffset StartedAt, EncounterStatus Status);

/// <summary>The verified chart of one patient (MDV-P2-004). Every fact carries its source; the AI summary is not part of it (P3).</summary>
public sealed record PatientChart(
    AllergyState AllergyState, IReadOnlyList<AllergyView> Allergies, IReadOnlyList<ProblemView> Problems, IReadOnlyList<MedicineView> Medicines,
    IReadOnlyList<VitalView> LastVitals, IReadOnlyList<TrendView> Trends, IReadOnlyList<RecentEncounter> RecentEncounters, Guid? OpenEncounterId);

/// <summary>
/// Patient 360's chart (MDV-P2-004). Needs clinical access to the patient at the given facility (role, care relationship, MDV-P2-024;
/// a refusal is a ClinicalAccessDeniedException) and writes its audit event with the patient before it answers.
/// </summary>
public interface IPatientChart
{
    Task<PatientChart> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);
}
