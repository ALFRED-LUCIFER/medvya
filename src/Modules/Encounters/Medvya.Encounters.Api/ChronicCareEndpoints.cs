using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

/// <summary>The chart's chronic care, with authors' names and the facility's time zone for showing days and times.</summary>
public sealed record ChronicCareResponse(bool Chronic, IReadOnlyList<CareTargetView> Targets, IReadOnlyList<CareGap> Gaps, IReadOnlyList<CareGoalView> Goals, bool RulesSignedOff, string TimeZone);

public static class ChronicCareEndpoints
{
    /// <summary>
    /// Chronic care targets, care gaps and goals (MDV-P2-038, E-01). Reading: a role with Patient 360; every write: Patient 360 at Full (a doctor).
    /// The use case then checks the care relationship at the session's facility and audits with the patient. Values travel in the body, never the URL.
    /// </summary>
    public static IEndpointRouteBuilder MapChronicCareEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/patients/{patientId:guid}/chronic-care", Get)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.care.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapPut("/v1/patients/{patientId:guid}/care-targets", SetTarget)
            .RequireCapability(Capability.Patient360Chart, Access.Full)
            .AuditedByUseCase("patient.care-target.set", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapPost("/v1/patients/{patientId:guid}/care-checks", RecordCheck)
            .RequireCapability(Capability.Patient360Chart, Access.Full)
            .AuditedByUseCase("patient.care-check.record", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapPost("/v1/patients/{patientId:guid}/care-goals", AddGoal)
            .RequireCapability(Capability.Patient360Chart, Access.Full)
            .AuditedByUseCase("patient.care-goal.save", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapPut("/v1/patients/{patientId:guid}/care-goals/{goalId:guid}", UpdateGoal)
            .RequireCapability(Capability.Patient360Chart, Access.Full)
            .AuditedByUseCase("patient.care-goal.save", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> Get(Guid patientId, [FromServices] IChronicCare service, [FromServices] IPatientDirectory patients, [FromServices] IStaffUsers users,
        [FromServices] IFacilityDirectory facilities, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!await KnownAsync(patients, patientId, ct)) return Results.NotFound();
        var view = await service.GetAsync(patientId, facilityId, ct);
        var names = await users.DisplayNamesAsync([.. view.Targets.Where(t => t.SetById is not null).Select(t => t.SetById!.Value)
            .Concat(view.Goals.SelectMany(g => new[] { g.CreatedById, g.UpdatedById })).Distinct()], ct);
        return Results.Ok(new ChronicCareResponse(view.Chronic,
            [.. view.Targets.Select(t => t.SetById is { } by ? t with { SetBy = names.GetValueOrDefault(by) } : t)], view.Gaps,
            [.. view.Goals.Select(g => Named(g, names))], view.RulesSignedOff,
            (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone));
    }

    static async Task<IResult> SetTarget(Guid patientId, [FromBody] SetCareTarget? body, [FromServices] IChronicCare service, [FromServices] IPatientDirectory patients,
        [FromServices] IStaffUsers users, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!await KnownAsync(patients, patientId, ct)) return Results.NotFound();
        var view = await service.SetTargetAsync(patientId, facilityId, body ?? new SetCareTarget(null, null), ct);
        return Results.Ok(view with { SetBy = (await users.DisplayNamesAsync([view.SetById!.Value], ct)).GetValueOrDefault(view.SetById!.Value) });
    }

    static async Task<IResult> RecordCheck(Guid patientId, [FromBody] RecordCareCheck? body, [FromServices] IChronicCare service, [FromServices] IPatientDirectory patients,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!await KnownAsync(patients, patientId, ct)) return Results.NotFound();
        await service.RecordCheckAsync(patientId, facilityId, body ?? new RecordCareCheck(null, null), ct);
        return Results.NoContent();
    }

    static async Task<IResult> AddGoal(Guid patientId, [FromBody] AddCareGoal? body, [FromServices] IChronicCare service, [FromServices] IPatientDirectory patients,
        [FromServices] IStaffUsers users, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!await KnownAsync(patients, patientId, ct)) return Results.NotFound();
        var goal = await service.AddGoalAsync(patientId, facilityId, body ?? new AddCareGoal(null, null, null), ct);
        return Results.Ok(Named(goal, await users.DisplayNamesAsync([goal.CreatedById], ct)));
    }

    static async Task<IResult> UpdateGoal(Guid patientId, Guid goalId, [FromBody] UpdateCareGoal? body, [FromServices] IChronicCare service, [FromServices] IPatientDirectory patients,
        [FromServices] IStaffUsers users, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!await KnownAsync(patients, patientId, ct)) return Results.NotFound();
        var goal = await service.UpdateGoalAsync(patientId, facilityId, goalId, body ?? new UpdateCareGoal(null), ct);
        return goal is null ? Results.NotFound() : Results.Ok(Named(goal, await users.DisplayNamesAsync([goal.CreatedById, goal.UpdatedById], ct)));
    }

    static CareGoalView Named(CareGoalView g, IReadOnlyDictionary<Guid, string> names) =>
        g with { CreatedBy = names.GetValueOrDefault(g.CreatedById), UpdatedBy = names.GetValueOrDefault(g.UpdatedById) };

    static async Task<bool> KnownAsync(IPatientDirectory patients, Guid patientId, CancellationToken ct) => (await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId);

    static IResult NoFacility() => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");

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
        catch (ChronicCareValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
    }
}
