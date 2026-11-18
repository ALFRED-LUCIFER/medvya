using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>What a dentist records on one tooth (MDV-P8-011, DN-02). <see cref="Sound"/> records that a tooth was checked and is healthy, so a later finding can clear an earlier one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ToothCondition>))]
public enum ToothCondition { Sound, Caries, Filling, Crown, RootCanalTreated, Missing, Implant, Bridge, Mobile, Fractured }

/// <summary>A treatment plan item's state (MDV-P8-012, DN-03). Done is final; a declined item stays on the plan with its reason and can be offered again.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DentalPlanStatus>))]
public enum DentalPlanStatus { Planned, InProgress, Done, Declined }

/// <summary>
/// The medical history questions asked before dental treatment (MDV-P8-014): blood thinners (anticoagulants or antiplatelets), diabetes, pregnancy, a heart
/// condition or endocarditis risk, bisphosphonates, and an allergy to local anaesthetic. A dentist approves the list (intake record).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DentalHistoryQuestion>))]
public enum DentalHistoryQuestion { BloodThinners, Diabetes, Pregnancy, HeartOrEndocarditisRisk, Bisphosphonates, LocalAnaestheticAllergy }

/// <summary>How the patient accepted the estimate: on the clinic tablet, or on the printed estimate signed by hand.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AcceptanceMethod>))]
public enum AcceptanceMethod { OnTablet, PrintedAndSigned }

/// <summary>
/// DENTAL_FINDING (MDV-P8-011): one condition on one tooth (FDI two-digit code) and, for caries and fillings, its surfaces, recorded by a dentist inside a
/// consultation. Append-only: a recorded finding is never edited or deleted. The tooth's state is its latest finding; a correction is a newer finding
/// (an addendum), so every earlier one stays in the tooth's history with who, when and the visit.
/// </summary>
public sealed class DentalFinding
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required string Tooth { get; init; }
    /// <summary>Surface letters in the order M, D, O, B, L ("OD" is stored "DO"); null when the condition is of the whole tooth.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Surfaces { get; init; }
    [Classification(DataClass.SensitiveHealth)] public ToothCondition Condition { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Note { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
}

/// <summary>
/// DENTAL_PLAN_ITEM (MDV-P8-012): one procedure of the patient's treatment plan, on a tooth (or the whole mouth) and its surfaces, in a phase, priced from the
/// services catalogue when planned (<see cref="EstimateMinor"/>, the estimate the patient sees). Never deleted: it moves Planned → In progress → Done, or to
/// Declined with a reason. A done item carries the visit it was done in (<see cref="DoneAppointmentId"/>); the visit's bill takes it as one suggested line
/// priced on the day (the fee version of the day), linked back to the item so it is billed once.
/// </summary>
public sealed class DentalPlanItem
{
    public const int MaxReason = 200, MaxServiceName = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    /// <summary>Opaque id of the service in the Billing module.</summary>
    /// <summary>The procedure consent this item needs before it starts (MDV-P8-019, <see cref="DentalConsentRules"/>), from the service's code when planned; null: none.</summary>
    [Classification(DataClass.Internal)] public string? ConsentKey { get; init; }
    [Classification(DataClass.Internal)] public Guid ServiceId { get; init; }
    /// <summary>The service's name when planned, so the plan reads the same after the catalogue is renamed.</summary>
    [Classification(DataClass.Public)] public required string ServiceName { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Tooth { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Surfaces { get; init; }
    [Classification(DataClass.Internal)] public int Phase { get; init; }
    [Classification(DataClass.Internal)] public long EstimateMinor { get; init; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.SensitiveHealth)] public DentalPlanStatus Status { get; set; }
    /// <summary>Why the item was declined or postponed; kept when it is offered again.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? Reason { get; set; }
    [Classification(DataClass.Internal)] public Guid CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
    /// <summary>Opaque id of the visit (Scheduling) whose consultation marked the item done.</summary>
    [Classification(DataClass.Internal)] public Guid? DoneAppointmentId { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? DoneAt { get; set; }
    /// <summary>Optimistic concurrency (PostgreSQL xmin): two dentists moving the same item at once, the second is told to reload.</summary>
    [Classification(DataClass.Internal)] public uint Version { get; set; }
}

/// <summary>
/// DENTAL_ESTIMATE_ACCEPTANCE (MDV-P8-012): the patient accepted the estimate as it stood: who accepted (the patient or a guardian, by name), how, when, the
/// total and a key of the items it covered (<see cref="DentalRules.EstimateKey"/>), so a plan changed since shows "changed since accepted". Append-only.
/// </summary>
public sealed class DentalEstimateAcceptance
{
    public const int MaxName = 120;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Personal)] public required string AcceptedByName { get; init; }
    [Classification(DataClass.Internal)] public AcceptanceMethod Method { get; init; }
    [Classification(DataClass.Internal)] public long TotalMinor { get; init; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.Internal)] public required string ItemsKey { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset AcceptedAt { get; init; }
}

/// <summary>
/// The rules of the tooth chart and the treatment plan. FDI two-digit numbering (ISO 3950): permanent teeth 11–18, 21–28, 31–38, 41–48; primary
/// teeth 51–55, 61–65, 71–75, 81–85. Surfaces M mesial, D distal, O occlusal (incisal on front teeth), B buccal or labial, L lingual or palatal.
/// shortcut: the condition list, which conditions take surfaces, and the plan's status moves follow the 06 Nov dental design; a dentist has not signed them
/// (MDV-P8-022, open questions "Conditions" and "Surfaces"). <see cref="SignOff"/> stays null and the screen says the chart rules are a draft until then.
/// </summary>
public static class DentalRules
{
    /// <summary>Null: the condition list and surface rules await a dentist's sign-off.</summary>
    public static readonly ClinicalSignOff? SignOff; // set to new(name, registration, date) when signed

    public const string SurfaceOrder = "MDOBL";
    public const int MaxNote = 200, MinPhase = 1, MaxPhase = 9, MaxHistory = 500;

    /// <summary>Conditions recorded on surfaces; every other condition is of the whole tooth and takes none.</summary>
    public static readonly IReadOnlySet<ToothCondition> SurfaceConditions = new HashSet<ToothCondition> { ToothCondition.Caries, ToothCondition.Filling };

    public static readonly IReadOnlyList<string> PermanentTeeth = [.. Quadrants(1, 4, 8)];
    public static readonly IReadOnlyList<string> PrimaryTeeth = [.. Quadrants(5, 8, 5)];
    static readonly HashSet<string> AllTeeth = [.. PermanentTeeth, .. PrimaryTeeth];

    static IEnumerable<string> Quadrants(int first, int last, int teeth) =>
        Enumerable.Range(first, last - first + 1).SelectMany(q => Enumerable.Range(1, teeth).Select(t => $"{q}{t}"));

    public static bool IsTooth(string? tooth) => tooth is not null && AllTeeth.Contains(tooth);

    /// <summary>The surfaces in canonical order, or null when the text is not a set of distinct surface letters (upper or lower case).</summary>
    public static string? Surfaces(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var letters = text.Trim().ToUpperInvariant();
        if (letters.Any(c => !SurfaceOrder.Contains(c, StringComparison.Ordinal)) || letters.Distinct().Count() != letters.Length) return null;
        return string.Concat(SurfaceOrder.Where(letters.Contains));
    }

    /// <summary>The (field, code) errors of a finding: "tooth:invalid", "condition:invalid", "surfaces:invalid|required|not_allowed", "note:too_long".</summary>
    public static List<(string Field, string Code)> Validate(string? tooth, ToothCondition? condition, string? surfaces, string? note)
    {
        var errors = new List<(string, string)>();
        if (!IsTooth(tooth)) errors.Add(("tooth", "invalid"));
        if (condition is not { } c || !Enum.IsDefined(c)) errors.Add(("condition", "invalid"));
        var s = Surfaces(surfaces);
        if (s is null) errors.Add(("surfaces", "invalid"));
        else if (condition is { } k && Enum.IsDefined(k))
        {
            if (SurfaceConditions.Contains(k) && s.Length == 0) errors.Add(("surfaces", "required"));
            else if (!SurfaceConditions.Contains(k) && s.Length > 0) errors.Add(("surfaces", "not_allowed"));
        }
        if (note?.Trim().Length > MaxNote) errors.Add(("note", "too_long"));
        return errors;
    }

    /// <summary>The (field, code) errors of a new plan item: an optional tooth, its surfaces and the phase.</summary>
    public static List<(string Field, string Code)> ValidatePlanItem(string? tooth, string? surfaces, int? phase)
    {
        var errors = new List<(string, string)>();
        if (tooth is not null && !IsTooth(tooth)) errors.Add(("tooth", "invalid"));
        var s = Surfaces(surfaces);
        if (s is null) errors.Add(("surfaces", "invalid"));
        else if (s.Length > 0 && tooth is null) errors.Add(("surfaces", "needs_tooth"));
        if (phase is not (>= MinPhase and <= MaxPhase)) errors.Add(("phase", "range"));
        return errors;
    }

    /// <summary>The moves a plan item may make. Done is final; Declined can be offered again (back to Planned).</summary>
    public static bool CanMove(DentalPlanStatus from, DentalPlanStatus to) => (from, to) switch
    {
        (DentalPlanStatus.Planned, DentalPlanStatus.InProgress) => true,
        (DentalPlanStatus.Planned, DentalPlanStatus.Declined) => true,
        (DentalPlanStatus.InProgress, DentalPlanStatus.Done) => true,
        (DentalPlanStatus.InProgress, DentalPlanStatus.Declined) => true,
        (DentalPlanStatus.Declined, DentalPlanStatus.Planned) => true,
        _ => false,
    };

    /// <summary>What the estimate covers: every item not declined.</summary>
    public static IEnumerable<DentalPlanItem> Estimated(IEnumerable<DentalPlanItem> items) => items.Where(i => i.Status != DentalPlanStatus.Declined);

    public static long EstimateTotal(IEnumerable<DentalPlanItem> items) => Estimated(items).Sum(i => i.EstimateMinor);

    /// <summary>A key of the estimated items and their prices (SHA-256, hex): equal keys mean the patient accepted this exact estimate.</summary>
    public static string EstimateKey(IEnumerable<DentalPlanItem> items)
    {
        var text = string.Join(";", Estimated(items).OrderBy(i => i.Id).Select(i => $"{i.Id:N}:{i.EstimateMinor}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>
/// DENTAL_HISTORY_CHECK (MDV-P8-014): the medical history that matters for dental care, confirmed with the patient in a consultation. Every question is
/// answered; <see cref="Yes"/> holds the ones answered yes (each raises a risk the dentist must see before treatment starts). Append-only: a later visit
/// confirms again with a new row, so the date it was last confirmed is the newest row's.
/// </summary>
public sealed class DentalHistoryCheck
{
    public const int MaxNote = 500;
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.SensitiveHealth)] public required DentalHistoryQuestion[] Yes { get; init; }
    [Classification(DataClass.SensitiveHealth)] public string? Note { get; init; }
    [Classification(DataClass.Internal)] public Guid ConfirmedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ConfirmedAt { get; init; }
}

/// <summary>The history check's rules (MDV-P8-014): every question answered yes or no, a short optional note.</summary>
public static class DentalHistoryRules
{
    public static readonly IReadOnlyList<DentalHistoryQuestion> Questions = Enum.GetValues<DentalHistoryQuestion>();

    public static List<(string Field, string Code)> Validate(IReadOnlyDictionary<DentalHistoryQuestion, bool>? answers, string? note)
    {
        var errors = new List<(string, string)>();
        foreach (var q in Questions)
            if (answers is null || !answers.ContainsKey(q)) errors.Add(($"answers.{q}", "required"));
        if (note?.Trim().Length > DentalHistoryCheck.MaxNote) errors.Add(("note", "too_long"));
        return errors;
    }

    /// <summary>The questions answered yes, in the order they are asked.</summary>
    public static DentalHistoryQuestion[] Yes(IReadOnlyDictionary<DentalHistoryQuestion, bool> answers) => [.. Questions.Where(q => answers[q])];
}

/// <summary>
/// DENTAL_CONSENT (MDV-P8-019): the patient's (or a guardian's) signed consent to one plan item's procedure, on the tablet, on the template version that was
/// current, in the language it was read in. The signed form is a PDF in the document store (<see cref="BlobKey"/>, <see cref="Sha256"/>). Never edited: only a
/// withdrawal is added to it, which stops the item from starting or finishing.
/// </summary>
public sealed class DentalConsent
{
    public const int MaxName = 200, MaxReason = 200;
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid? MergedFromPatientId { get; init; }
    [Classification(DataClass.Internal)] public Guid PlanItemId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    [Classification(DataClass.Internal)] public required string TemplateKey { get; init; }
    [Classification(DataClass.Internal)] public int TemplateVersion { get; init; }
    [Classification(DataClass.Internal)] public required string Language { get; init; }
    [Classification(DataClass.Personal)] public required string SignedByName { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SignedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public required string BlobKey { get; init; }
    [Classification(DataClass.Internal)] public required string Sha256 { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? WithdrawnAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? WithdrawnBy { get; set; }
    [Classification(DataClass.SensitiveHealth)] public string? WithdrawalReason { get; set; }
}

/// <summary>
/// The procedure consent templates (MDV-P8-019): which procedures need one, by the starter list's service codes, and each template's current version. The
/// wording is in the catalogue (dentalConsent.&lt;key&gt;.*) in English, Hindi and Arabic. A draft until a lawyer and a dentist approve it (intake record):
/// <see cref="Approved"/> is null until then and the screen and the form say so. A changed wording is a new version; a consent on an older one must be
/// signed again.
/// </summary>
public static class DentalConsentRules
{
    public const string Extraction = "extraction", RootCanal = "rootCanal", Implant = "implant", Sedation = "sedation";
    public static readonly IReadOnlyDictionary<string, int> Versions = new Dictionary<string, int> { [Extraction] = 1, [RootCanal] = 1, [Implant] = 1, [Sedation] = 1 };
    public static readonly IReadOnlyList<string> Languages = ["en", "hi", "ar"];
    /// <summary>Who approved the wording and when (a lawyer and a dentist); null: a draft.</summary>
    public static string? Approved => null;
    public const int MaxSignatureBytes = 300_000;

    /// <summary>The consent a service needs, from its code (DentalStarterList); null for any other service. shortcut: by code until the services list carries the consent itself.</summary>
    public static string? For(string? serviceCode) => serviceCode switch
    {
        "DN-SRG001" or "DN-SRG002" or "DN-SRG003" or "DN-SRG004" => Extraction,
        "DN-END001" or "DN-END002" or "DN-END003" or "DN-END004" => RootCanal,
        "DN-PRS011" or "DN-PRS013" => Implant,
        _ => null,
    };

    /// <summary>A PNG image (by its signature bytes), not empty and not too large.</summary>
    public static bool IsSignature(byte[]? png) => png is { Length: > 8 and <= MaxSignatureBytes } && png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
}
