using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

/// <summary>A medicine as the screens show it: its class and what a printed prescription needs for that class (stable codes the UI translates).</summary>
public sealed record FormularyItemView(
    Guid Id, string Generic, string Strength, string Form, IReadOnlyList<string> Brands, string? NationalCode, RegulatoryClass RegClass,
    IReadOnlyList<string> PrintRequirements, string? Atc, string? DefaultSig, FormularySource Source, bool Active);

/// <summary>A medicine the admin adds. Blank stays blank: no national code, ATC or default sig is guessed.</summary>
public sealed record AddMedicine(string? Generic, string? Strength, string? Form, IReadOnlyList<string>? Brands, string? NationalCode, RegulatoryClass? RegClass, string? Atc, string? DefaultSig);

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
