using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

public sealed record DecideMedicine(string? Key, MedicationDecisionKind? Kind, string? NewDose, string? Reason);

public sealed record MedicineDecisionView(MedicationDecisionKind Kind, string? NewDose, string? Reason, DateTimeOffset DecidedAt);

/// <summary>One medicine across the sources. A side is null when that source does not mention it. <see cref="Other"/> is an import; the HIS column arrives with P7.</summary>
public sealed record MedicineRow(
    string Key, string Name, MedicineSide? Chart, MedicineSide? Patient, MedicineSide? Other, MedicineConflict? Conflict,
    IReadOnlyList<MedicationDecisionKind> Allowed, MedicineDecisionView? Decision);

/// <summary>"n of m decided · k sources disagree". <see cref="ConfirmedAt"/> set: the decisions were applied to the chart and the table is read-only.</summary>
public sealed record ReconciliationView(Guid EncounterId, Guid PatientId, IReadOnlyList<MedicineRow> Rows, int Decided, int Total, int Disagree, DateTimeOffset? ConfirmedAt);

public sealed class ReconciliationValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Reconciliation input is invalid.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>"ended" (the consultation is over), "confirmed" (already applied) or "undecided" (a medicine still has no decision).</summary>
public sealed class ReconciliationConflictException(string code) : Exception("Reconciliation conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Medication reconciliation (MDV-P2-011): chart vs patient-reported vs other sources, decided medicine by medicine, applied to the chart on confirm.
/// A doctor's capability: every call needs clinical access to the encounter's patient and writes its audit event (no medicine names or reasons in it).
/// </summary>
public interface IMedicationReconciliation
{
    /// <summary>Null: no such encounter in this tenant.</summary>
    Task<ReconciliationView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default);

    /// <summary>Records the decision for one medicine (the latest replaces the current one; earlier ones stay as history).</summary>
    /// <exception cref="ReconciliationValidationException"/>
    /// <exception cref="ReconciliationConflictException"/>
    Task<ReconciliationView?> DecideAsync(Guid encounterId, DecideMedicine command, CancellationToken cancellationToken = default);

    /// <summary>Applies every decision to the chart. Refused until every medicine is decided.</summary>
    /// <exception cref="ReconciliationConflictException"/>
    Task<ReconciliationView?> ConfirmAsync(Guid encounterId, CancellationToken cancellationToken = default);
}

/// <summary>A medicine the doctor decided to stop in an encounter's reconciliation (its latest decision), with the reason given.</summary>
public sealed record StoppedMedicine(string Name, string? Reason);

/// <summary>
/// The stop decisions of an encounter's reconciliation, for the prescription composer (MDV-P2-015), which shows each as a discontinue line.
/// No access check and no audit event here: the caller has already checked clinical access to the encounter's patient and audits its own read.
/// </summary>
public interface IReconciliationStops
{
    Task<IReadOnlyList<StoppedMedicine>> StopsAsync(Guid encounterId, CancellationToken cancellationToken = default);
}

/// <summary>A substance on the chart (never a "no known allergies" entry, never one ruled "not an allergy"), with its status for the evidence.</summary>
public sealed record ChartAllergy(Guid Id, string Substance, AllergyStatus Status);

/// <summary>An active medicine on the chart, as named there.</summary>
public sealed record ChartMedicine(Guid Id, string Name);

/// <summary>A reading on the chart: the value in hundredths of its unit (14.2 kg is 1420) and when it was taken.</summary>
public sealed record ChartReading(int ValueHundredths, DateTimeOffset TakenAt);

/// <summary>
/// What a prescription is checked against: the allergy state (three different things, never merged), the listed allergies, the active medicines
/// and the latest weight and eGFR (null: none on the chart; the checks decide whether a weight is recent enough).
/// </summary>
public sealed record PrescribingFacts(AllergyState AllergyState, IReadOnlyList<ChartAllergy> Allergies, IReadOnlyList<ChartMedicine> ActiveMedicines, ChartReading? Weight, ChartReading? Egfr);

/// <summary>
/// The chart facts for the prescription safety checks (MDV-P2-016, MDV-P2-040). No access check and no audit event here: the caller has already checked
/// clinical access to the patient and audits its own check.
/// </summary>
public interface IPrescribingChart
{
    Task<PrescribingFacts> FactsAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>The pregnancy state for the checks (MDV-P2-034), on the facility's date.</summary>
    Task<ChartPregnancy> PregnancyAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The pregnancy facts the checks read: the completed weeks of an open pregnancy, or a "not pregnant" statement that holds today. Neither is
/// "not recorded", never "not pregnant".
/// </summary>
public sealed record ChartPregnancy(int? GestationWeeks, bool RuledOut);
