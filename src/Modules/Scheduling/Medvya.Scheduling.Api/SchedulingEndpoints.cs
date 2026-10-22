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

        // Confirming the patient before booking opens their name and MRN, so the patient id is in the route and in the audit row.
        schedule.MapGet("/patients/{patientId:guid}", async (Guid patientId, Guid? facilityId, [FromServices] IBookingService service, CancellationToken ct) =>
                await service.FindPatientAsync(facilityId ?? Guid.Empty, patientId, ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .Audited("patient.lookup", "patient", "treatment", patientRouteKey: "patientId");

        var holds = app.MapGroup("/v1/slot-holds").RequireAuthorization().AddEndpointFilter(MapErrors);
        holds.MapPost("/", (HoldSlot command, [FromServices] IBookingService service, CancellationToken ct) => service.HoldAsync(command, ct))
            .Audited("slot.hold", "slot_hold");
        holds.MapDelete("/{holdId:guid}", async (Guid holdId, [FromServices] IBookingService service, CancellationToken ct) =>
            {
                await service.ReleaseHoldAsync(holdId, ct);
                return Results.NoContent();
            })
            .Audited("slot.release", "slot_hold");

        var appointments = app.MapGroup("/v1/appointments").RequireAuthorization().AddEndpointFilter(MapErrors);
        appointments.MapPost("/", (BookAppointment command, [FromServices] IBookingService service, CancellationToken ct) => service.BookAsync(command, ct))
            .Audited("appointment.book", "appointment");
        // The reception board (MDV-P1-028). A repeated check-in or move returns the visit unchanged; any other move is a 409.
        appointments.MapPost("/{appointmentId:guid}/check-in", (Guid appointmentId, CheckIn? command, [FromServices] IFrontDeskService service, CancellationToken ct) =>
                service.CheckInAsync(appointmentId, command ?? new CheckIn(), ct))
            .Audited("appointment.check_in", "appointment");
        appointments.MapPost("/{appointmentId:guid}/status", (Guid appointmentId, MoveVisit command, [FromServices] IFrontDeskService service, CancellationToken ct) =>
                service.MoveAsync(appointmentId, command, ct))
            .Audited("appointment.status", "appointment");

        var frontDesk = app.MapGroup("/v1/front-desk").RequireAuthorization().AddEndpointFilter(MapErrors);
        frontDesk.MapGet("/board", (Guid? facilityId, [FromServices] IFrontDeskService service, CancellationToken ct) =>
                service.GetBoardAsync(facilityId ?? Guid.Empty, ct))
            .Audited("front_desk.board.read", "appointment");

        return app;
    }

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
