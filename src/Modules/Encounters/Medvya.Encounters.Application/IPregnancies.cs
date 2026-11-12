using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>The dates of a pregnancy: the LMP, a dating scan (its date and the gestation it showed, in weeks and days), or both.</summary>
public sealed record PregnancyDates(DateOnly? Lmp, DateOnly? ScanDate, int? ScanWeeks, int? ScanDays);

/// <summary>A new pregnancy: its dates and the obstetric history before it (gravida counts this pregnancy). A count left out stays unknown.</summary>
public sealed record StartPregnancy(DateOnly? Lmp, DateOnly? ScanDate, int? ScanWeeks, int? ScanDays, int? Gravida, int? Para, int? Abortions, int? Living);

public sealed record ClosePregnancy(PregnancyOutcome? Outcome);

/// <summary>Fundal height in cm and fetal heart rate per minute, one decimal place at most; a blank stays blank.</summary>
public sealed record PregnancyReadings(decimal? FundalHeightCm, decimal? FetalHeartRate);

/// <summary>A planned antenatal contact: its week, its state and, when done, the visit (encounter) linked to it.</summary>
public sealed record AntenatalContactView(int Week, ContactState State, Guid? EncounterId);

/// <summary>
/// The open pregnancy as the chart shows it. <see cref="GestationDays"/> is today's, on the facility's date. <see cref="PreviousDueDate"/> is
/// the due date that a later change of dates replaced. <see cref="RulesSignedOff"/> false: the dating rule and contact plan await clinical review.
/// </summary>
public sealed record PregnancyView(
    Guid Id, DateOnly? Lmp, DateOnly? ScanDate, int? ScanGestationDays, DateOnly DueDate, DatingMethod DatedBy, DatingReason DatingReason, DateOnly? PreviousDueDate,
    int GestationDays, int? Gravida, int? Para, int? Abortions, int? Living, string? StartedBy, DateTimeOffset StartedAt,
    IReadOnlyList<AntenatalContactView> Contacts, IReadOnlyList<DangerSign> DangerSigns, string? DangerSignsCounselledBy, DateTimeOffset? DangerSignsCounselledAt,
    bool RulesSignedOff);

/// <summary>A "not pregnant" statement that still holds today, with who made it and when.</summary>
public sealed record RuledOutView(string? RecordedBy, DateTimeOffset RecordedAt);

/// <summary>The patient's pregnancy state: an open pregnancy, a "not pregnant" statement for today, or neither (not recorded, never "not pregnant").</summary>
public sealed record PregnancyStatus(PregnancyView? Open, RuledOutView? RuledOut);

/// <summary>A pregnancy input the server refuses. Field/code pairs only, never the entered values.</summary>
public sealed class PregnancyValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Pregnancy input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>"already_open" (one open pregnancy per patient), "no_open_pregnancy", "pregnancy_open" (it cannot be ruled out) or "no_open_visit".</summary>
public sealed class PregnancyConflictException(string code) : Exception("Pregnancy conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// The pregnancy record (MDV-P2-034). Every write needs clinical access to the patient at the facility and writes its audit event with the
/// patient: starting, re-dating and closing a pregnancy for a doctor (clinical notes at Sign); danger-sign counselling, "not pregnant" and the
/// antenatal readings for a doctor or a nurse (vitals and allergies entry at Full). Every write answers the new state.
/// </summary>
public interface IPregnancies
{
    /// <summary>The state for the chart. No access check and no audit here: the chart read has done both.</summary>
    Task<PregnancyStatus> StatusAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="PregnancyValidationException"/>
    /// <exception cref="PregnancyConflictException">"already_open".</exception>
    Task<PregnancyStatus> StartAsync(Guid patientId, Guid facilityId, StartPregnancy command, CancellationToken cancellationToken = default);

    /// <summary>New dates for the open pregnancy; a due date they move is kept as the previous one.</summary>
    Task<PregnancyStatus> DateAsync(Guid patientId, Guid facilityId, PregnancyDates command, CancellationToken cancellationToken = default);

    Task<PregnancyStatus> CloseAsync(Guid patientId, Guid facilityId, ClosePregnancy command, CancellationToken cancellationToken = default);

    Task<PregnancyStatus> CounselDangerSignsAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="PregnancyConflictException">"pregnancy_open".</exception>
    Task<PregnancyStatus> RuleOutAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Records the readings on the patient's open consultation at the facility, like the nurse's vitals.</summary>
    /// <exception cref="PregnancyConflictException">"no_open_pregnancy", "no_open_visit".</exception>
    Task<PregnancyStatus> RecordReadingsAsync(Guid patientId, Guid facilityId, PregnancyReadings command, CancellationToken cancellationToken = default);
}
