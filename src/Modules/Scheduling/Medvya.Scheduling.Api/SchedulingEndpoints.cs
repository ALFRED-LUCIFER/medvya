using Medvya.Audit.Api;
using Medvya.Scheduling.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Scheduling.Api;

/// <summary>
/// Scheduling HTTP endpoints (docs/03-architecture/api-sketch.md). Sign-in is required here; the capability check against the
/// user's membership at the facility runs inside each use case. Every request is audited before it runs (fail closed).
/// Query strings carry opaque ids and dates only, never patient data.
/// </summary>
public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        // Group filters run outside endpoint filters, so the audit row is written before any error is mapped.
        var schedule = app.MapGroup("/v1/schedule").RequireAuthorization().AddEndpointFilter(MapErrors);

        schedule.MapGet("/", (Guid? facilityId, DateOnly? date, [FromServices] IScheduleService service, CancellationToken ct) =>
                service.GetDayAsync(facilityId ?? Guid.Empty, date, ct))
            .Audited("schedule.read", "schedule");

        schedule.MapGet("/templates", (Guid? facilityId, [FromServices] IScheduleService service, CancellationToken ct) =>
                service.ListTemplatesAsync(facilityId ?? Guid.Empty, ct))
            .Audited("schedule.template.read", "schedule_template", "operations");

        schedule.MapPut("/templates", (SaveTemplate command, [FromServices] IScheduleService service, CancellationToken ct) => service.SaveTemplateAsync(command, ct))
            .Audited("schedule.template.update", "schedule_template", "operations");

        schedule.MapPost("/resources", (AddResource command, [FromServices] IScheduleService service, CancellationToken ct) => service.AddResourceAsync(command, ct))
            .Audited("schedule.resource.create", "schedule_resource", "operations");

        return app;
    }

    /// <summary>Use-case errors as problem details: field/code pairs for 400, a generic 403 that never says why.</summary>
    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SchedulingValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (SchedulingAccessDeniedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No access at this facility");
        }
    }
}
