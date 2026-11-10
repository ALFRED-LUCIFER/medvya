using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Prescribing.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Prescribing.Api;

public static class PrescriptionEndpoints
{
    /// <summary>
    /// The draft prescription composer (MDV-P2-015). Signed in with "Prescriptions and orders" at Sign; the use case then checks clinical access
    /// to the encounter's patient and writes the audit event with the patient. Ids only in paths; medicine names and dose fields travel in bodies.
    /// No route here authorises a prescription (MDV-P2-017).
    /// </summary>
    public static IEndpointRouteBuilder MapPrescriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireCapability(Capability.PrescriptionsOrders, Access.Sign);

        g.MapGet("/encounters/{encounterId:guid}/prescription", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.ListAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.read", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/items", async (Guid encounterId, [FromBody] AddRxLine? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.AddAsync(encounterId, body ?? new AddRxLine(null, null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.add", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPut("/prescription-items/{itemId:guid}", async (Guid itemId, [FromBody] UpdateRxLine? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.UpdateAsync(itemId, body ?? new UpdateRxLine(null, null, null, null, null, null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.update", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPut("/prescription-items/{itemId:guid}/included", async (Guid itemId, [FromBody] IncludeRxLine? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.IncludeAsync(itemId, body ?? new IncludeRxLine(null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.include", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // Safety checks (MDV-P2-016): run after every change; an override carries its reason in the body.
        g.MapGet("/encounters/{encounterId:guid}/prescription/checks", async (Guid encounterId, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.CheckAsync(encounterId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.check", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/encounters/{encounterId:guid}/prescription/overrides", async (Guid encounterId, [FromBody] OverrideRxAlert? body, [FromServices] IPrescriptions service, CancellationToken ct) =>
                await service.OverrideAsync(encounterId, body ?? new OverrideRxAlert(null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("rx.override", "prescription", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        return app;
    }

    // Inside the audit filter (added after it), so the row records the answer the caller gets.
    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ClinicalAccessDeniedException e)
        {
            return ClinicalAccessProblem.For(e);
        }
        catch (PrescriptionValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (PrescriptionConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The prescription cannot change now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
