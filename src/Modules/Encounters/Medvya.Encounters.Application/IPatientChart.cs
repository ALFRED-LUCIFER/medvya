using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>Allergy state for the banner: three different things, never merged. Listed also covers "unknown" entries (they are shown, not hidden).</summary>
public enum AllergyState { NotRecorded, NoKnownAllergies, Listed }

public sealed record AllergyView(string Substance, string? Reaction, AllergyStatus Status, ChartSource Source);
/// <summary>The banner's allergy answer: the state and, when listed, the allergies (MDV-P2-004).</summary>
public sealed record AllergyBanner(AllergyState State, IReadOnlyList<AllergyView> Allergies);

/// <summary>One patient's allergy state and whether a nurse's reading in the last day was out of range (MDV-1090), for a list of visits (Today). No substances: the list shows a flag, the chart shows the detail.</summary>
public sealed record AllergyFlag(Guid PatientId, AllergyState State, bool AbnormalVitals);

public sealed record ProblemView(string? Icd10Code, string Description, ProblemVerification Verification, DateOnly? OnsetOn, ChartSource Source, ProblemStatus Status = ProblemStatus.Active);
/// <summary><see cref="EncounterId"/>: the visit whose authorised prescription put it on the chart (MDV-1087); its printed prescription is that visit's.</summary>
public sealed record MedicineView(string Name, string? Dose, MedicationState State, ChartSource Source, DateTimeOffset? StoppedAt = null, Guid? EncounterId = null);
/// <summary>
/// The latest reading of one code, a vital or a lab. <see cref="Flag"/> is High or Low against the adult <see cref="Range"/> (MDV-P2-031,
/// awaiting clinical sign-off); a code without a range has neither.
/// </summary>
public sealed record VitalView(ObservationCode Code, decimal Value, string Unit, DateTimeOffset TakenAt, ChartSource Source, VitalAlert? Flag = null, string? Range = null);
/// <summary>BMI worked out from the latest weight and the latest height, never stored. The chart names the older of the two and its date.</summary>
public sealed record BmiView(decimal Value, ObservationCode OlderCode, DateTimeOffset OlderAt);
public sealed record TrendPoint(decimal Value, DateTimeOffset TakenAt, ChartSource Source);
/// <summary>The last readings of one code, oldest first, for every code with a reading. Blood pressure is two series (systolic and diastolic).</summary>
public sealed record TrendView(ObservationCode Code, string Unit, IReadOnlyList<TrendPoint> Points);
public sealed record RecentEncounter(Guid Id, DateTimeOffset StartedAt, EncounterStatus Status);

/// <summary>
/// The verified chart of one patient (MDV-P2-004). Every fact carries its source; the AI summary is not part of it (P3). History is every saved
/// version, newest first; <see cref="BloodGroup"/> is the current blood group, null when never recorded; <see cref="CareTeam"/> is the
/// clinicians who led the patient's consultations, most recent first (MDV-P2-032).
/// </summary>
public sealed record PatientChart(
    AllergyState AllergyState, IReadOnlyList<AllergyView> Allergies, IReadOnlyList<ProblemView> Problems, IReadOnlyList<MedicineView> Medicines,
    IReadOnlyList<VitalView> LastVitals, IReadOnlyList<TrendView> Trends, IReadOnlyList<RecentEncounter> RecentEncounters, Guid? OpenEncounterId,
    IReadOnlyList<VitalView> LastLabs, BmiView? Bmi,
    IReadOnlyList<HistoryEntryView> History, string? BloodGroup, IReadOnlyList<string> CareTeam);

/// <summary>
/// Patient 360's chart (MDV-P2-004). Needs clinical access to the patient at the given facility (role, care relationship, MDV-P2-024;
/// a refusal is a ClinicalAccessDeniedException) and writes its audit event with the patient before it answers.
/// </summary>
public interface IPatientChart
{
    Task<PatientChart> GetAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The allergy state of each patient the caller may treat at the facility; a patient without access is left out, never refused or guessed.
    /// One audit event per patient answered. At most <see cref="MaxFlags"/> ids.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="MaxFlags"/> ids.</exception>
    Task<IReadOnlyList<AllergyFlag>> AllergyFlagsAsync(IReadOnlyList<Guid> patientIds, Guid facilityId, CancellationToken cancellationToken = default);

    public const int MaxFlags = 100;
}
