using Medvya.Audit.Api;
using Medvya.Scheduling.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Scheduling.Api;

/// <summary>
/// Scheduling HTTP endpoints (docs/03-architecture/api-sketch.md). Sign-in is required here; the capability check against the
/// user's membership at the facility runs inside each use case. Every request is audited before its answer is sent (fail closed).
/// Query strings carry opaque ids and dates only, never patient data.
/// </summary>
public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var schedule = app.MapGroup("/v1/schedule").RequireAuthorization();

        schedule.MapGet("/", (Guid? facilityId, DateOnly? date, [FromServices] IScheduleService service, CancellationToken ct) =>
                service.GetDayAsync(facilityId ?? Guid.Empty, date, ct))
            .AuditedWithErrors("schedule.read", "schedule");

        schedule.MapGet("/week", (Guid? facilityId, Guid? calendarId, DateOnly? date, [FromServices] IScheduleService service, CancellationToken ct) =>
                service.GetWeekAsync(facilityId ?? Guid.Empty, calendarId ?? Guid.Empty, date, ct))
            .AuditedWithErrors("schedule.read", "schedule");

        schedule.MapGet("/templates", (Guid? facilityId, [FromServices] IScheduleService service, CancellationToken ct) =>
                service.ListTemplatesAsync(facilityId ?? Guid.Empty, ct))
            .AuditedWithErrors("schedule.template.read", "schedule_template", "operations");

        schedule.MapPut("/templates", (SaveTemplate command, [FromServices] IScheduleService service, CancellationToken ct) => service.SaveTemplateAsync(command, ct))
            .AuditedWriteWithErrors(SchedulingAudit.TemplateUpdate, SchedulingAudit.TemplateResource, SchedulingAudit.Operations);

        schedule.MapPost("/resources", (AddResource command, [FromServices] IScheduleService service, CancellationToken ct) => service.AddResourceAsync(command, ct))
            .AuditedWriteWithErrors(SchedulingAudit.ResourceCreate, SchedulingAudit.ResourceResource, SchedulingAudit.Operations);

        // Confirming the patient before booking opens their name and MRN, so the patient id is in the route and in the audit row.
        schedule.MapGet("/patients/{patientId:guid}", async (Guid patientId, Guid? facilityId, [FromServices] IBookingService service, CancellationToken ct) =>
                await service.FindPatientAsync(facilityId ?? Guid.Empty, patientId, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .AuditedWithErrors("patient.lookup", "patient", "registration", patientRouteKey: "patientId"); // the purpose front-desk search uses (MDV-P1-018)

        var holds = app.MapGroup("/v1/slot-holds").RequireAuthorization();
        holds.MapPost("/", (HoldSlot command, [FromServices] IBookingService service, CancellationToken ct) => service.HoldAsync(command, ct))
            .AuditedWriteWithErrors(SchedulingAudit.SlotHold, SchedulingAudit.HoldResource, SchedulingAudit.Treatment);
        holds.MapDelete("/{holdId:guid}", async (Guid holdId, [FromServices] IBookingService service, CancellationToken ct) =>
            {
                await service.ReleaseHoldAsync(holdId, ct);
                return Results.NoContent();
            })
            .AuditedWriteWithErrors(SchedulingAudit.SlotRelease, SchedulingAudit.HoldResource, SchedulingAudit.Treatment);

        var appointments = app.MapGroup("/v1/appointments").RequireAuthorization();
        appointments.MapPost("/", (BookAppointment command, [FromServices] IBookingService service, CancellationToken ct) => service.BookAsync(command, ct))
            .AuditedWriteWithErrors(SchedulingAudit.Book, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment);
        // The reception board (MDV-P1-028). A repeated check-in or move returns the visit unchanged; any other move is a 409.
        appointments.MapPost("/{appointmentId:guid}/check-in", (Guid appointmentId, CheckIn? command, [FromServices] IFrontDeskService service, CancellationToken ct) =>
                service.CheckInAsync(appointmentId, command ?? new CheckIn(), ct))
            .AuditedWriteWithErrors(SchedulingAudit.CheckIn, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment);
        appointments.MapPost("/{appointmentId:guid}/status", (Guid appointmentId, MoveVisit command, [FromServices] IFrontDeskService service, CancellationToken ct) =>
                service.MoveAsync(appointmentId, command, ct))
            .AuditedWriteWithErrors(SchedulingAudit.Status, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment);

        var frontDesk = app.MapGroup("/v1/front-desk").RequireAuthorization();
        frontDesk.MapGet("/board", (Guid? facilityId, [FromServices] IFrontDeskService service, CancellationToken ct) =>
                service.GetBoardAsync(facilityId ?? Guid.Empty, ct))
            .AuditedWithErrors("front_desk.board.read", "appointment");

        return app;
    }

    /// <summary>
    /// Audits the endpoint and maps use-case errors to results <b>inside</b> the audit filter, so the audit row records the answer
    /// the caller gets (a refused request is Denied, an unknown appointment NotFound). A group filter would run outside the
    /// audit filter, which would then see an exception and record a refused request as Allowed.
    /// </summary>
    static RouteHandlerBuilder AuditedWithErrors(this RouteHandlerBuilder endpoint, string action, string resourceType, string purpose = "treatment", string? patientRouteKey = null) =>
        endpoint.Audited(action, resourceType, purpose, patientRouteKey).AddEndpointFilter(MapErrors);

    /// <summary>
    /// A write: the use case writes the allowed event in the transaction of its change (<see cref="SchedulingAudit"/> codes);
    /// a refused, rejected or not-found attempt changed nothing and is audited by the endpoint, errors mapped inside as above.
    /// </summary>
    static RouteHandlerBuilder AuditedWriteWithErrors(this RouteHandlerBuilder endpoint, string action, string resourceType, string purpose) =>
        endpoint.AuditedByUseCase(action, resourceType, purpose).AddEndpointFilter(MapErrors);

    /// <summary>Use-case errors as problem details: field/code pairs for 400, a generic 403 that never says why, 409 with a code.</summary>
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
        catch (SlotUnavailableException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Slot no longer available", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
        catch (VisitTransitionException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Visit cannot make this move", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
        catch (AppointmentNotFoundException)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Appointment not found");
        }
    }
}
