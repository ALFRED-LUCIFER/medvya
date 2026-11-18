using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>A finding as entered in the consultation: an FDI tooth code ("36"), the condition, surface letters for caries and fillings ("OD"), an optional note.</summary>
public sealed record RecordToothFinding(string? Tooth, ToothCondition? Condition, string? Surfaces, string? Note);

/// <summary>One recorded finding. <see cref="Author"/> is filled by the endpoint from <see cref="RecordedBy"/>.</summary>
public sealed record ToothFindingView(
    Guid Id, string Tooth, ToothCondition Condition, string? Surfaces, string? Note, Guid EncounterId, Guid RecordedBy, DateTimeOffset RecordedAt, string? Author = null);

/// <summary>A procedure added to the plan: a catalogue service, optionally on a tooth and its surfaces, in a phase (1 to 9).</summary>
public sealed record AddDentalPlanItem(Guid? ServiceId, string? Tooth, string? Surfaces, int? Phase);

/// <summary>
/// Moves a plan item. <see cref="Reason"/> is required to decline. <see cref="EncounterId"/> is required to start or finish: the patient's open
/// consultation, whose visit a done item is billed to. <see cref="Version"/> is the version the screen read (409 when stale).
/// </summary>
public sealed record MoveDentalPlanItem(DentalPlanStatus? To, string? Reason, Guid? EncounterId, uint? Version);

public sealed record DentalPlanItemView(
    Guid Id, Guid ServiceId, string ServiceName, string? Tooth, string? Surfaces, int Phase, long EstimateMinor, string Currency, DentalPlanStatus Status,
    string? Reason, DateTimeOffset CreatedAt, DateTimeOffset ChangedAt, DateTimeOffset? DoneAt, uint Version);

/// <summary>The patient (or a guardian) accepts the estimate as it stands now.</summary>
public sealed record AcceptDentalEstimate(string? AcceptedBy, AcceptanceMethod? Method);

public sealed record DentalAcceptanceView(Guid Id, string AcceptedBy, AcceptanceMethod Method, long TotalMinor, string Currency, DateTimeOffset AcceptedAt, Guid RecordedBy);

/// <summary>
/// The plan: items in phase order, the estimate total of every item not declined, the latest acceptance, and whether that acceptance still covers the plan
/// as it stands (<see cref="AcceptanceCurrent"/> false: the plan changed since and the patient should accept again).
/// </summary>
public sealed record DentalPlanView(IReadOnlyList<DentalPlanItemView> Items, long TotalMinor, string? Currency, DentalAcceptanceView? Acceptance, bool AcceptanceCurrent);

/// <summary>
/// The tooth chart: the latest finding of every tooth that has one (<see cref="Teeth"/>), every finding newest first (<see cref="History"/>, at most
/// <see cref="DentalRules.MaxHistory"/>), and the plan. A tooth with no finding is "not recorded", never healthy. <see cref="OpenEncounterId"/>: the caller's
/// open consultation with the patient at this facility, where findings are recorded and items started or finished (null: none open).
/// </summary>
public sealed record DentalChartView(IReadOnlyList<ToothFindingView> Teeth, IReadOnlyList<ToothFindingView> History, DentalPlanView Plan, Guid? OpenEncounterId = null);

/// <summary>A catalogue service a dentist can plan, with the clinic's price now (null: not priced, cannot be planned).</summary>
public sealed record DentalServiceOption(Guid Id, string Name, string? Code, int? DurationMinutes, long? PriceMinor, string Currency);

/// <summary>Field/code pairs (never the entered values), as the screen translates them.</summary>
public sealed class DentalValidationException(IReadOnlyList<(string Field, string Code)> errors) : Exception("Invalid dental input.")
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The item changed since the screen read it (another dentist moved it): reload.</summary>
public sealed class DentalConflictException() : Exception("The plan item changed since it was read.");

/// <summary>The chart and plan as an A4 PDF: the dental record and estimate the clinic prints or hands over.</summary>
public sealed record DentalPdf(byte[] Content);

/// <summary>
/// The dental chart and treatment plan (MDV-P8-011, MDV-P8-012) of one patient at the session's facility. Reading needs clinical access to the chart;
/// recording findings and changing the plan need a doctor who signs clinical notes. Every use case checks the care relationship (a
/// ClinicalAccessDeniedException) and audits with the patient. Rows are never deleted.
/// </summary>
public interface IDental
{
    Task<DentalChartView> ChartAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Records a finding in the patient's open consultation; null when the consultation is not the patient's or not at this facility.</summary>
    /// <exception cref="DentalValidationException">Field errors, or "encounter:ended" when the consultation has ended (findings belong to an open visit).</exception>
    Task<ToothFindingView?> RecordFindingAsync(Guid encounterId, Guid facilityId, RecordToothFinding command, CancellationToken cancellationToken = default);

    /// <summary>The services a dentist can plan: active consultations, procedures and tests of the catalogue, with today's clinic price.</summary>
    Task<IReadOnlyList<DentalServiceOption>> ServicesAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="DentalValidationException">Field errors; "serviceId:unknown" or "serviceId:no_price".</exception>
    Task<DentalPlanItemView> AddPlanItemAsync(Guid patientId, Guid facilityId, AddDentalPlanItem command, CancellationToken cancellationToken = default);

    /// <summary>Null when the item is not the patient's.</summary>
    /// <exception cref="DentalValidationException">"to:not_allowed", "reason:required", "encounterId:required|invalid|ended".</exception>
    /// <exception cref="DentalConflictException"/>
    Task<DentalPlanItemView?> MovePlanItemAsync(Guid patientId, Guid facilityId, Guid itemId, MoveDentalPlanItem command, CancellationToken cancellationToken = default);

    /// <exception cref="DentalValidationException">"acceptedBy:required|too_long", "method:invalid", "plan:empty".</exception>
    Task<DentalAcceptanceView> AcceptEstimateAsync(Guid patientId, Guid facilityId, AcceptDentalEstimate command, CancellationToken cancellationToken = default);

    /// <summary>The chart and estimate as an A4 PDF in the print family frame.</summary>
    /// <exception cref="NotePdfUnsupportedTextException"/>
    Task<DentalPdf> PdfAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);
}

/// <summary>A done plan item a visit's bill takes as a suggested line (MDV-P8-012): ids only, never the tooth or any clinical detail.</summary>
public sealed record DentalCharge(Guid PlanItemId, Guid ServiceId);

/// <summary>
/// What Billing asks of the dental plan: the items done in a visit. Billing prices them on the day and links each line back to its item, so an item is
/// billed once. Runs as the desk (no clinical access check): the answer carries ids only.
/// </summary>
public interface IDentalCharges
{
    Task<IReadOnlyList<DentalCharge>> DoneInVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}
