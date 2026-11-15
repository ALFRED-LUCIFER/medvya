using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

/// <summary>
/// Scheduled or controlled class of a medicine. India uses H, H1 and X (Drugs and Cosmetics Rules); the UAE uses Controlled. None is an ordinary
/// prescription medicine. NotVerified (MDV-P5-064): nobody has confirmed the class yet (a register such as CDCI carries none), so it is never
/// treated as None: the composer and the paper say the class is not verified.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegulatoryClass>))]
public enum RegulatoryClass { None, ScheduleH, ScheduleH1, ScheduleX, Controlled, NotVerified }

/// <summary>
/// Starter: the seeded list (ADR-015, pending clinical review). ClinicAdded: a medicine the clinic's admin added. Medvya: added to the country's
/// shared list by the Medvya team in the console (MDV-P5-064). Register: loaded from a national register's release (MDV-P5-066).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FormularySource>))]
public enum FormularySource { Starter, ClinicAdded, Medvya, Register }

/// <summary>
/// One medicine (generic, strength and form) in a country's formulary (MDV-P2-014). A row with no tenant belongs to the country's starter list,
/// which every clinic of that country reads and none can change; a row with a tenant is that clinic's own addition.
/// </summary>
public sealed class FormularyItem
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid? TenantId { get; init; }
    [Classification(DataClass.Internal)] public string Country { get; init; } = "";
    /// <summary>The generic name (INN).</summary>
    [Classification(DataClass.Internal)] public string Generic { get; init; } = "";
    [Classification(DataClass.Internal)] public string Strength { get; init; } = "";
    [Classification(DataClass.Internal)] public string Form { get; init; } = "";
    [Classification(DataClass.Internal)] public string[] Brands { get; init; } = [];
    /// <summary>The national register's code (the UAE drug code). Blank when the register is not loaded: never guessed.</summary>
    [Classification(DataClass.Internal)] public string? NationalCode { get; init; }
    [Classification(DataClass.Internal)] public RegulatoryClass RegClass { get; init; }
    [Classification(DataClass.Internal)] public string? Atc { get; init; }
    [Classification(DataClass.Internal)] public string? DefaultSig { get; init; }
    [Classification(DataClass.Internal)] public FormularySource Source { get; init; }
    [Classification(DataClass.Internal)] public bool Active { get; init; } = true;
    [Classification(DataClass.Internal)] public Guid? AddedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? AddedAt { get; init; }
    /// <summary>The register's own code for a row loaded from it (source Register), unique per country: a later release updates the row with that code.</summary>
    [Classification(DataClass.Internal)] public string? RegisterCode { get; init; }
    /// <summary>The register release that last listed the code; a code missing from a newer release is made inactive, never deleted.</summary>
    [Classification(DataClass.Internal)] public string? RegisterRelease { get; init; }
}

/// <summary>
/// A brand name the clinic gave a formulary medicine (MDV-P1-088). The brand is a name for that row, never a medicine of its own: a line
/// prescribed by brand is the generic's row, so it is checked exactly as the generic is. The row may be a starter one; the brand is the clinic's.
/// </summary>
public sealed class FormularyBrand
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FormularyItemId { get; init; }
    [Classification(DataClass.Internal)] public string Name { get; init; } = "";
    [Classification(DataClass.Internal)] public Guid? AddedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset AddedAt { get; init; }
}

/// <summary>A named list of investigations the doctor orders together, such as "Diabetes review labs" (MDV-P2-014; used by the order composer, MDV-P2-018).</summary>
public sealed class OrderSet
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public string Name { get; init; } = "";
    [Classification(DataClass.Internal)] public string[] Items { get; init; } = [];
    [Classification(DataClass.Internal)] public Guid CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Formulary rules per country (AGENTS.md 3.3): one subclass per country, no country checks elsewhere. What class a medicine may carry,
/// what a printed prescription must show for it, and the shape of the national code.
/// shortcut: print requirements are stable codes the screens translate and the Rx print (MDV-P2-017) will follow; the wording of each needs sign-off by a regulatory advisor.
/// </summary>
public abstract class FormularyRules
{
    public static readonly FormularyRules India = new IndiaFormulary();
    public static readonly FormularyRules Uae = new UaeFormulary();

    public static FormularyRules For(string countryCode) => Find(countryCode) ?? throw new ArgumentOutOfRangeException(nameof(countryCode), "Unsupported country.");

    /// <summary>The rules of a country Medvya runs in, or null (a country code a caller sent).</summary>
    public static FormularyRules? Find(string? countryCode) => countryCode switch
    {
        "IN" => India,
        "AE" => Uae,
        _ => null,
    };

    /// <summary>The paper's line for a medicine whose class nobody has confirmed: never printed as an ordinary medicine (MDV-P5-064).</summary>
    public const string ClassNotVerified = "class_not_verified";

    /// <summary>Dose forms a medicine may be added in.</summary>
    public static readonly IReadOnlySet<string> Forms = new HashSet<string>(StringComparer.Ordinal)
    {
        "Tablet", "Capsule", "Syrup", "Suspension", "Injection", "Cream", "Ointment", "Drops", "Inhaler", "Patch", "Suppository", "Powder", "Solution",
    };

    static readonly Regex AtcShape = new(@"^[A-Z]\d{2}[A-Z]{2}\d{2}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool IsAtc(string value) => AtcShape.IsMatch(value);

    /// <summary>The classes this country uses (a method, not a property: domain properties are stored fields and carry a data classification).</summary>
    public abstract IReadOnlySet<RegulatoryClass> Classes();

    /// <summary>What a printed prescription needs for this class (empty for an ordinary medicine): warning text and who keeps a copy.</summary>
    public abstract IReadOnlyList<string> PrintRequirements(RegulatoryClass regClass);

    /// <summary>Whether the national code has the register's shape. Blank is allowed (the caller never passes it).</summary>
    public abstract bool IsNationalCode(string value);

    sealed class IndiaFormulary : FormularyRules
    {
        static readonly HashSet<RegulatoryClass> Used = [RegulatoryClass.None, RegulatoryClass.ScheduleH, RegulatoryClass.ScheduleH1, RegulatoryClass.ScheduleX, RegulatoryClass.NotVerified];

        public override IReadOnlySet<RegulatoryClass> Classes() => Used;

        public override IReadOnlyList<string> PrintRequirements(RegulatoryClass regClass) => regClass switch
        {
            RegulatoryClass.ScheduleH => ["warning_schedule_h"],
            RegulatoryClass.ScheduleH1 => ["warning_schedule_h1", "pharmacist_keeps_copy"],
            RegulatoryClass.ScheduleX => ["warning_schedule_x", "pharmacist_keeps_copy", "duplicate_prescription"],
            RegulatoryClass.NotVerified => [ClassNotVerified],
            _ => [],
        };

        // India has no single national drug code; a clinic may record its own reference.
        public override bool IsNationalCode(string value) => value.Length <= 30 && value.All(char.IsAsciiLetterOrDigit);
    }

    sealed class UaeFormulary : FormularyRules
    {
        static readonly HashSet<RegulatoryClass> Used = [RegulatoryClass.None, RegulatoryClass.Controlled, RegulatoryClass.NotVerified];

        public override IReadOnlySet<RegulatoryClass> Classes() => Used;

        public override IReadOnlyList<string> PrintRequirements(RegulatoryClass regClass) => regClass switch
        {
            RegulatoryClass.Controlled => ["warning_controlled", "pharmacist_keeps_copy"],
            RegulatoryClass.NotVerified => [ClassNotVerified],
            _ => [],
        };

        // The register's drug codes are letters, digits and dashes (for example "A12-3456-789").
        public override bool IsNationalCode(string value) => value.Length is >= 3 and <= 30 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
}
