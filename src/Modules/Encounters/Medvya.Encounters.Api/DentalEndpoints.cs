using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

/// <summary>
/// The tooth chart with its authors' names, the plan and the time zone. <see cref="RulesSignedOff"/> false: the condition list and surface rules are a draft
/// awaiting a dentist (the screen says so).
/// </summary>
public sealed record DentalChartResponse(
    IReadOnlyList<ToothFindingView> Teeth, IReadOnlyList<ToothFindingView> History, DentalPlanView Plan, Guid? OpenEncounterId, string TimeZone, bool RulesSignedOff,
    DentalHistoryCheckView? HistoryCheck = null);

public static class DentalEndpoints
{
    /// <summary>
    /// The dental pack (MDV-P8-011, MDV-P8-012, DN-02, DN-03), behind the clinic's dental switch. Reading: a role with Patient 360; recording findings,
    /// planning, moving items and recording the patient's acceptance: a doctor who signs clinical notes. The use case then checks the care relationship at
    /// the session's facility and audits with the patient. Clinical content travels in the body, never the URL.
    /// </summary>
    public static IEndpointRouteBuilder MapDentalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/patients/{patientId:guid}/dental", Chart)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.dental.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapGet("/v1/patients/{patientId:guid}/dental/services", Services)
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapPost("/v1/encounters/{encounterId:guid}/dental/findings", Record)
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("patient.dental.finding", "patient", AuditPurpose.Treatment)
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapPost("/v1/encounters/{encounterId:guid}/dental/history-check", ConfirmHistory)
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("patient.dental.history_check", "patient", AuditPurpose.Treatment)
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapPost("/v1/patients/{patientId:guid}/dental/plan", Plan)
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("patient.dental.plan", "dental_plan", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapPost("/v1/patients/{patientId:guid}/dental/plan/{itemId:guid}/move", Move)
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("patient.dental.plan_move", "dental_plan", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapPost("/v1/patients/{patientId:guid}/dental/estimate/acceptance", Accept)
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("patient.dental.estimate_accept", "dental_plan", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        app.MapPost("/v1/patients/{patientId:guid}/dental/pdf", Pdf)
            .RequireCapability(Capability.Patient360Chart, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.dental.pdf", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors).RequireFeature(Features.Dental);
        return app;
    }

    static async Task<IResult> Chart(Guid patientId, [FromServices] IDental service, [FromServices] IPatientDirectory patients, [FromServices] IStaffUsers users,
        [FromServices] IFacilityDirectory facilities, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        var chart = await service.ChartAsync(patientId, facilityId, ct);
        var names = await users.DisplayNamesAsync([.. chart.History.Select(f => f.RecordedBy).Concat(chart.HistoryCheck is { } c ? [c.ConfirmedBy] : []).Distinct()], ct);
        ToothFindingView Named(ToothFindingView f) => f with { Author = names.GetValueOrDefault(f.RecordedBy) };
        return Results.Ok(new DentalChartResponse([.. chart.Teeth.Select(Named)], [.. chart.History.Select(Named)], chart.Plan, chart.OpenEncounterId,
            (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone, DentalRules.SignOff is not null,
            chart.HistoryCheck is { } check ? check with { Author = names.GetValueOrDefault(check.ConfirmedBy) } : null));
    }

    static async Task<IResult> Services(Guid patientId, [FromServices] IDental service, [FromServices] IPatientDirectory patients, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        return Results.Ok(await service.ServicesAsync(patientId, facilityId, ct));
    }

    static async Task<IResult> Record(Guid encounterId, [FromBody] RecordToothFinding? body, [FromServices] IDental service, [FromServices] IStaffUsers users,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (body is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["finding"] = ["empty"] });
        if (await service.RecordFindingAsync(encounterId, facilityId, body, ct) is not { } row) return Results.NotFound();
        return Results.Ok(row with { Author = (await users.DisplayNamesAsync([row.RecordedBy], ct)).GetValueOrDefault(row.RecordedBy) });
    }

    static async Task<IResult> ConfirmHistory(Guid encounterId, [FromBody] ConfirmDentalHistory? body, [FromServices] IDental service, [FromServices] IStaffUsers users,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (body is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["check"] = ["empty"] });
        if (await service.ConfirmHistoryAsync(encounterId, facilityId, body, ct) is not { } row) return Results.NotFound();
        return Results.Ok(row with { Author = (await users.DisplayNamesAsync([row.ConfirmedBy], ct)).GetValueOrDefault(row.ConfirmedBy) });
    }

    static async Task<IResult> Plan(Guid patientId, [FromBody] AddDentalPlanItem? body, [FromServices] IDental service, [FromServices] IPatientDirectory patients,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        if (body is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["item"] = ["empty"] });
        return Results.Ok(await service.AddPlanItemAsync(patientId, facilityId, body, ct));
    }

    static async Task<IResult> Move(Guid patientId, Guid itemId, [FromBody] MoveDentalPlanItem? body, [FromServices] IDental service, [FromServices] IPatientDirectory patients,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        if (body is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["move"] = ["empty"] });
        return await service.MovePlanItemAsync(patientId, facilityId, itemId, body, ct) is { } item ? Results.Ok(item) : Results.NotFound();
    }

    static async Task<IResult> Accept(Guid patientId, [FromBody] AcceptDentalEstimate? body, [FromServices] IDental service, [FromServices] IPatientDirectory patients,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        if (body is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["acceptance"] = ["empty"] });
        return Results.Ok(await service.AcceptEstimateAsync(patientId, facilityId, body, ct));
    }

    static async Task<IResult> Pdf(Guid patientId, [FromServices] IDental service, [FromServices] IPatientDirectory patients, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return NoFacility();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        return Results.File((await service.PdfAsync(patientId, facilityId, ct)).Content, "application/pdf");
    }

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
        catch (DentalValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (DentalConflictException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The plan changed; reload");
        }
        catch (NotePdfUnsupportedTextException)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Text the PDF cannot show");
        }
    }
}
