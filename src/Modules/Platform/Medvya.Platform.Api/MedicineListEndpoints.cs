using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Platform.Api;

/// <summary>
/// The Medvya-wide medicine list (MDV-P5-064, ADR-014 note): each country's shared formulary rows, which every clinic of that country reads. Everything
/// goes through <see cref="ISharedFormulary"/>, on its own narrow login that row-level security limits to rows with no tenant. Every console role reads;
/// owner and operations add, correct, set a class and deactivate, each with a reason that goes to the platform audit log.
/// </summary>
public static class MedicineListEndpoints
{
    const string Resource = "medicine";

    public sealed record NewMedicine(string? Country, string? Generic, string? Strength, string? Form, IReadOnlyList<string>? Brands, string? NationalCode,
        RegulatoryClass? RegClass, string? Atc, string? DefaultSig, string? Reason) : IReasoned
    {
        public AddMedicine Medicine() => new(Generic, Strength, Form, Brands, NationalCode, RegClass, Atc, DefaultSig);
    }

    /// <summary>Names, strength, form, brands and codes; the class has its own action.</summary>
    public sealed record Correction(string? Generic, string? Strength, string? Form, IReadOnlyList<string>? Brands, string? NationalCode, string? Atc,
        string? DefaultSig, string? Reason) : IReasoned
    {
        public AddMedicine Medicine() => new(Generic, Strength, Form, Brands, NationalCode, null, Atc, DefaultSig);
    }

    public sealed record ClassChange(RegulatoryClass? RegClass, string? Reason) : IReasoned;

    public static WebApplication MapMedicineList(this WebApplication app)
    {
        // The query holds a medicine name and a country, never a patient value.
        app.MapGet("/v1/medicines", async (string? country, string? q, int? limit, [FromServices] ISharedFormulary list, CancellationToken ct) =>
            Results.Ok(await list.SearchAsync(country, q, limit ?? 50, ct))).RequireAuthorization(PlatformPolicies.MedicinesRead).AddEndpointFilter(MapErrors);

        // shortcut: the change and its platform audit entry are in two logins (medvya_formulary and the console's), so they cannot share a transaction;
        // the change is made first. Add an outbox or a reconcile job if an entry here is ever missing (as for organisation decisions).
        var act = app.Actions("/v1/medicines").RequireAuthorization(PlatformPolicies.MedicinesWrite).AddEndpointFilter(MapErrors);
        act.MapPost("/", async ([FromBody] NewMedicine body, [FromServices] ISharedFormulary list, [FromServices] IConsoleActions actions, CancellationToken ct) =>
        {
            var added = await list.AddAsync(body.Country, body.Medicine(), ct);
            await actions.RecordAsync("medicine.add", Resource, added.Id, null, PlatformReason.Valid(body.Reason), ct);
            return Results.Created((string?)null, added);
        });
        act.MapPost("/{id:guid}/correction", async (Guid id, [FromBody] Correction body, [FromServices] ISharedFormulary list, [FromServices] IConsoleActions actions, CancellationToken ct) =>
            await Recorded(await list.CorrectAsync(id, body.Medicine(), ct), "medicine.correct", body, actions, ct));
        act.MapPost("/{id:guid}/class", async (Guid id, [FromBody] ClassChange body, [FromServices] ISharedFormulary list, [FromServices] IConsoleActions actions, CancellationToken ct) =>
            await Recorded(await list.SetClassAsync(id, body.RegClass, ct), "medicine.class", body, actions, ct));
        act.MapPost("/{id:guid}/deactivation", async (Guid id, [FromBody] PlatformEndpoints.ReasonBody body, [FromServices] ISharedFormulary list, [FromServices] IConsoleActions actions, CancellationToken ct) =>
            await Recorded(await list.DeactivateAsync(id, ct), "medicine.deactivate", body, actions, ct));
        return app;
    }

    static async Task<IResult> Recorded(SharedMedicineView? changed, string action, IReasoned body, IConsoleActions actions, CancellationToken ct)
    {
        if (changed is null) return Results.NotFound();
        await actions.RecordAsync(action, Resource, changed.Id, null, PlatformReason.Valid(body.Reason), ct);
        return Results.Ok(changed);
    }

    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try { return await next(ctx); }
        catch (FormularyValidationException e) { return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray())); }
        catch (FormularyConflictException e) { return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already in the list", extensions: new Dictionary<string, object?> { ["code"] = e.Code }); }
    }
}
