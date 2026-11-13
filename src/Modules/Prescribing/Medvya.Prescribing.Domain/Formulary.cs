using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

/// <summary>Scheduled or controlled class of a medicine. India uses H, H1 and X (Drugs and Cosmetics Rules); the UAE uses Controlled. None is an ordinary prescription medicine.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegulatoryClass>))]
public enum RegulatoryClass { None, ScheduleH, ScheduleH1, ScheduleX, Controlled }

/// <summary>Starter: the seeded list (ADR-015, pending clinical review). ClinicAdded: a medicine the clinic's admin added.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FormularySource>))]
public enum FormularySource { Starter, ClinicAdded }

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

    public static FormularyRules For(string countryCode) => countryCode switch
    {
        "IN" => India,
        "AE" => Uae,
        _ => throw new ArgumentOutOfRangeException(nameof(countryCode), "Unsupported country."),
    };

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
        static readonly HashSet<RegulatoryClass> Used = [RegulatoryClass.None, RegulatoryClass.ScheduleH, RegulatoryClass.ScheduleH1, RegulatoryClass.ScheduleX];

        public override IReadOnlySet<RegulatoryClass> Classes() => Used;

        public override IReadOnlyList<string> PrintRequirements(RegulatoryClass regClass) => regClass switch
        {
            RegulatoryClass.ScheduleH => ["warning_schedule_h"],
            RegulatoryClass.ScheduleH1 => ["warning_schedule_h1", "pharmacist_keeps_copy"],
            RegulatoryClass.ScheduleX => ["warning_schedule_x", "pharmacist_keeps_copy", "duplicate_prescription"],
            _ => [],
        };

        // India has no single national drug code; a clinic may record its own reference.
        public override bool IsNationalCode(string value) => value.Length <= 30 && value.All(char.IsAsciiLetterOrDigit);
    }

    sealed class UaeFormulary : FormularyRules
    {
        static readonly HashSet<RegulatoryClass> Used = [RegulatoryClass.None, RegulatoryClass.Controlled];

        public override IReadOnlySet<RegulatoryClass> Classes() => Used;

        public override IReadOnlyList<string> PrintRequirements(RegulatoryClass regClass) =>
            regClass == RegulatoryClass.Controlled ? ["warning_controlled", "pharmacist_keeps_copy"] : [];

        // The register's drug codes are letters, digits and dashes (for example "A12-3456-789").
        public override bool IsNationalCode(string value) => value.Length is >= 3 and <= 30 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
}
