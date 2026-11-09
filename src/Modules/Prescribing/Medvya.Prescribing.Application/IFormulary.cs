using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Application;

/// <summary>A medicine as the screens show it: its class and what a printed prescription needs for that class (stable codes the UI translates).</summary>
public sealed record FormularyItemView(
    Guid Id, string Generic, string Strength, string Form, IReadOnlyList<string> Brands, string? NationalCode, RegulatoryClass RegClass,
    IReadOnlyList<string> PrintRequirements, string? Atc, string? DefaultSig, FormularySource Source, bool Active);

/// <summary>A medicine the admin adds. Blank stays blank: no national code, ATC or default sig is guessed.</summary>
public sealed record AddMedicine(string? Generic, string? Strength, string? Form, IReadOnlyList<string>? Brands, string? NationalCode, RegulatoryClass? RegClass, string? Atc, string? DefaultSig);

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
    /// <summary>Active medicines whose generic or brand contains <paramref name="query"/> (blank lists the first ones), best matches first.</summary>
    Task<IReadOnlyList<FormularyItemView>> SearchAsync(string? query, int limit, CancellationToken cancellationToken = default);

    Task<FormularyItemView> AddAsync(AddMedicine command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSetView>> OrderSetsAsync(CancellationToken cancellationToken = default);

    Task<OrderSetView> CreateOrderSetAsync(CreateOrderSet command, CancellationToken cancellationToken = default);
}
