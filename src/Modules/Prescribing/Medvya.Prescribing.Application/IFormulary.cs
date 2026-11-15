using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

/// <summary>A medicine as the screens show it: its class and what a printed prescription needs for that class (stable codes the UI translates).</summary>
public sealed record FormularyItemView(
    Guid Id, string Generic, string Strength, string Form, IReadOnlyList<string> Brands, string? NationalCode, RegulatoryClass RegClass,
    IReadOnlyList<string> PrintRequirements, string? Atc, string? DefaultSig, FormularySource Source, bool Active);

/// <summary>A medicine the admin adds. Blank stays blank: no national code, ATC or default sig is guessed.</summary>
public sealed record AddMedicine(string? Generic, string? Strength, string? Form, IReadOnlyList<string>? Brands, string? NationalCode, RegulatoryClass? RegClass, string? Atc, string? DefaultSig);

/// <summary>A medicine the country's rules accepted: trimmed, its brands without blanks or repeats, a blank optional field null.</summary>
public sealed record ValidMedicine(string Generic, string Strength, string Form, string[] Brands, string? NationalCode, RegulatoryClass RegClass, string? Atc, string? DefaultSig);

/// <summary>The add rules, one copy for a clinic's own medicine (MDV-P2-014) and the country's shared list in the console (MDV-P5-064).</summary>
public static class MedicineInput
{
    public const int MaxBrands = 10, MaxBrand = 100;

    /// <summary>Every refused field with its code at once (<see cref="FormularyValidationException"/>), or the medicine as it will be stored.</summary>
    public static ValidMedicine Validate(AddMedicine command, FormularyRules rules)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(rules);
        var errors = new List<(string, string)>();
        var generic = Required(command.Generic, "generic", 200, errors);
        var strength = Required(command.Strength, "strength", 50, errors);
        var form = command.Form?.Trim();
        if (string.IsNullOrEmpty(form)) errors.Add(("form", "required"));
        else if (!FormularyRules.Forms.Contains(form)) errors.Add(("form", "unknown"));
        var brands = (command.Brands ?? []).Select(b => b?.Trim() ?? "").Where(b => b.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (brands.Length > MaxBrands) errors.Add(("brands", "too_many"));
        else if (brands.Any(b => b.Length > MaxBrand)) errors.Add(("brands", "too_long"));
        // Blank is allowed and stays blank; a value must have the country's shape.
        var code = Optional(command.NationalCode);
        if (code is not null && !rules.IsNationalCode(code)) errors.Add(("nationalCode", "invalid"));
        var atc = Optional(command.Atc)?.ToUpperInvariant();
        if (atc is not null && !FormularyRules.IsAtc(atc)) errors.Add(("atc", "invalid"));
        var sig = Optional(command.DefaultSig);
        if (sig?.Length > 200) errors.Add(("defaultSig", "too_long"));
        // The class is chosen on purpose: an ordinary medicine is "None", never the default of a missing answer.
        if (command.RegClass is not { } regClass) errors.Add(("regClass", "required"));
        else if (!rules.Classes().Contains(regClass)) errors.Add(("regClass", "not_in_country"));
        if (errors.Count > 0) throw new FormularyValidationException(errors);
        return new(generic!, strength!, form!, brands, code, command.RegClass!.Value, atc, sig);
    }

    public static string? Required(string? value, string field, int max, List<(string, string)> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) errors.Add((field, "required"));
        else if (v.Length > max) errors.Add((field, "too_long"));
        return v;
    }

    static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The text as a literal inside an ILIKE pattern (backslash is PostgreSQL's default escape).</summary>
    public static string LikeLiteral(string q)
    {
        ArgumentNullException.ThrowIfNull(q);
        return q.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    }
}

/// <summary>
/// One brand to add (MDV-P1-088): the brand, the formulary medicine it names (generic, strength and form, which must already be listed), and
/// the allergy class its reviewer expects ("none" for no class). The class is checked against the safety rules, never stored or guessed.
/// </summary>
public sealed record BrandRow(string? Brand, string? Generic, string? Strength, string? Form, string? AllergyClass);

public sealed record AddBrands(IReadOnlyList<BrandRow>? Rows);

public sealed record BrandsAdded(int Added);

public sealed record OrderSetView(Guid Id, string Name, IReadOnlyList<string> Items);

public sealed record CreateOrderSet(string? Name, IReadOnlyList<string>? Items);

/// <summary>Input refused. Fields and codes only, never what was typed.</summary>
public sealed class FormularyValidationException(IReadOnlyList<(string Field, string Code)> errors)
    : Exception("Formulary input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<(string Field, string Code)> Errors { get; } = errors;
}

/// <summary>The medicine or order set already exists in this clinic's list.</summary>
public sealed class FormularyConflictException(string code) : Exception("Formulary conflict: " + code)
{
    public string Code { get; } = code;
}

/// <summary>
/// The country formulary and the clinic's order sets (MDV-P2-014). Reads are for the clinic's own country (the tenant's) and its own
/// additions; the starter list is read-only. The endpoint checks the role; adds are audited in the same transaction.
/// </summary>
public interface IFormulary
{
    /// <summary>Active medicines with a word of the generic or of a brand starting with <paramref name="query"/> (blank lists the first ones), names that start with it first.</summary>
    Task<IReadOnlyList<FormularyItemView>> SearchAsync(string? query, int limit, CancellationToken cancellationToken = default);

    Task<FormularyItemView> AddAsync(AddMedicine command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the clinic's brand names to medicines already listed, all or none: a row whose medicine is not listed, has no ingredient code (so
    /// would not be allergy-checked) or whose allergy class differs from the safety rules' refuses the whole request, with a code per row.
    /// </summary>
    Task<BrandsAdded> AddBrandsAsync(AddBrands command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSetView>> OrderSetsAsync(CancellationToken cancellationToken = default);

    Task<OrderSetView> CreateOrderSetAsync(CreateOrderSet command, CancellationToken cancellationToken = default);
}

/// <summary>A row of a country's shared list as the console shows it (MDV-P5-064): no clinic's brands, no tenant.</summary>
public sealed record SharedMedicineView(
    Guid Id, string Country, string Generic, string Strength, string Form, IReadOnlyList<string> Brands, string? NationalCode, RegulatoryClass RegClass,
    string? Atc, string? DefaultSig, FormularySource Source, string? RegisterCode, string? RegisterRelease, bool Active);

/// <summary>
/// The country's shared medicine list for the platform console (MDV-P5-064, ADR-014 note): the formulary rows with no tenant, which every clinic of
/// that country reads. It runs on its own narrow login (medvya_formulary), which row-level security limits to those rows; it never sees a clinic's own
/// medicines or brands. The console checks the role and the reason and writes the platform audit entry.
/// </summary>
public interface ISharedFormulary
{
    /// <summary>Active and inactive rows of <paramref name="country"/> matching as the clinic search does (start of a word of the generic or a brand).</summary>
    Task<IReadOnlyList<SharedMedicineView>> SearchAsync(string? country, string? query, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds a Medvya-wide medicine under the clinic add's rules (<see cref="MedicineInput"/>); refused when the country already lists it.</summary>
    Task<SharedMedicineView> AddAsync(string? country, AddMedicine command, CancellationToken cancellationToken = default);

    /// <summary>Corrects names, strength, form, brands and codes; the class stays as it is (<see cref="SetClassAsync"/>). Null when there is no such shared row.</summary>
    Task<SharedMedicineView?> CorrectAsync(Guid id, AddMedicine command, CancellationToken cancellationToken = default);

    /// <summary>Sets the class after an advisor's review. Null when there is no such shared row.</summary>
    Task<SharedMedicineView?> SetClassAsync(Guid id, RegulatoryClass? regClass, CancellationToken cancellationToken = default);

    /// <summary>Stops offering the medicine; it is never deleted (prescriptions point at it). Null when there is no such shared row.</summary>
    Task<SharedMedicineView?> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
}
