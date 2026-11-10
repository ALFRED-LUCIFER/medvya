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

public static class ProblemEndpoints
{
    /// <summary>
    /// Diagnosis coding and the problem list (MDV-P2-010, E-07). A doctor's capability (Patient 360 at Full); the use case then checks clinical
    /// access to the encounter's patient and audits with the patient. Ids are the only things in a path: a code or description is in the body.
    /// The code set is the whole country list, searched in the browser, so a search term never reaches a URL or a log.
    /// </summary>
    public static IEndpointRouteBuilder MapProblemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/diagnosis-codes", async ([FromServices] IProblemList service, CancellationToken ct) => Results.Ok(await service.CodeSetAsync(ct)))
            .RequireCapability(Capability.Patient360Chart, Access.Full);

        var group = app.MapGroup("/v1/encounters/{encounterId:guid}/problems").RequireCapability(Capability.Patient360Chart, Access.Full);
        group.MapGet("/", async (Guid encounterId, [FromServices] IProblemList service, CancellationToken ct) =>
                await service.GetAsync(encounterId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("problem.list.read", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        group.MapPost("/", async (Guid encounterId, [FromBody] AddProblem? body, [FromServices] IProblemList service, CancellationToken ct) =>
                await service.AddAsync(encounterId, body ?? new AddProblem(null, null, null, null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("problem.add", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        group.MapPut("/{problemId:guid}", async (Guid encounterId, Guid problemId, [FromBody] UpdateProblem? body, [FromServices] IProblemList service, CancellationToken ct) =>
                await service.UpdateAsync(encounterId, problemId, body ?? new UpdateProblem(null, null, null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("problem.update", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
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
        catch (ProblemValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (ProblemConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The problem list cannot do this now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
