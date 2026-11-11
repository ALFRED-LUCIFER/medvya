using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

public static class FollowUpEndpoints
{
    /// <summary>The plan's follow-up (MDV-P2-043). A doctor's capability (Patient 360 at Full); the use case checks clinical access and audits with the patient.</summary>
    public static IEndpointRouteBuilder MapFollowUpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/encounters/{encounterId:guid}/follow-up").RequireCapability(Capability.Patient360Chart, Access.Full);
        group.MapGet("/", async (Guid encounterId, [FromServices] IFollowUps service, CancellationToken ct) =>
                await service.GetAsync(encounterId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("encounter.followup.read", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        group.MapPut("/", async (Guid encounterId, [FromBody] SetFollowUp? body, [FromServices] IFollowUps service, CancellationToken ct) =>
                await service.SetAsync(encounterId, body ?? new SetFollowUp(null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("encounter.followup.set", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
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
        catch (FollowUpValidationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["invalid"] });
        }
        catch (VisitTransitionException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The follow-up cannot change now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
