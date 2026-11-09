using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

public static class ReconciliationEndpoints
{
    /// <summary>
    /// Medication reconciliation (MDV-P2-011). A doctor's capability (Patient 360 at Full); the use case then checks clinical access to the encounter's
    /// patient and audits with the patient. The encounter id is the only thing in a path: a medicine's name is in the body, never a URL.
    /// </summary>
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/encounters/{encounterId:guid}/medicines/reconciliation").RequireCapability(Capability.Patient360Chart, Access.Full);

        group.MapGet("/", async (Guid encounterId, [FromServices] IMedicationReconciliation service, CancellationToken ct) =>
                await service.GetAsync(encounterId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("medication.reconcile.read", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        group.MapPost("/decisions", async (Guid encounterId, [FromBody] DecideMedicine? body, [FromServices] IMedicationReconciliation service, CancellationToken ct) =>
                await service.DecideAsync(encounterId, body ?? new DecideMedicine(null, null, null, null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("medication.reconcile.decide", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        group.MapPost("/confirm", async (Guid encounterId, [FromServices] IMedicationReconciliation service, CancellationToken ct) =>
                await service.ConfirmAsync(encounterId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("medication.reconcile.confirm", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
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
        catch (ReconciliationValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (ReconciliationConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Reconciliation cannot do this now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
