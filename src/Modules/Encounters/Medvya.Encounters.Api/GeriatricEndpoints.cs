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

/// <summary>The patient's latest older adult reviews, newest first, with the author's name. <see cref="RulesSignedOff"/> false: the flag lines are a draft.</summary>
public sealed record GeriatricAssessmentsResponse(IReadOnlyList<GeriatricAssessmentView> Assessments, string TimeZone, bool RulesSignedOff);

public static class GeriatricEndpoints
{
    /// <summary>
    /// The older adult review (MDV-P2-037, E-01). Reading: a role with Patient 360; recording: vitals entry at Full (a nurse or a doctor). The use
    /// case then checks the care relationship at the session's facility and audits with the patient. Scores travel in the body, never the URL.
    /// </summary>
    public static IEndpointRouteBuilder MapGeriatricEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/patients/{patientId:guid}/geriatric-assessments", List)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.geriatric.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapPost("/v1/patients/{patientId:guid}/geriatric-assessments", Record)
            .RequireCapability(Capability.VitalsAllergiesEntry, Access.Full)
            .AuditedByUseCase("patient.geriatric.record", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> List(Guid patientId, [FromServices] IGeriatricAssessments service, [FromServices] IPatientDirectory patients, [FromServices] IStaffUsers users,
        [FromServices] IFacilityDirectory facilities, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        var rows = await service.ListAsync(patientId, facilityId, ct);
        var names = await users.DisplayNamesAsync([.. rows.Select(r => r.RecordedBy).Distinct()], ct);
        return Results.Ok(new GeriatricAssessmentsResponse([.. rows.Select(r => r with { Author = names.GetValueOrDefault(r.RecordedBy) })],
            (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone, GeriatricRules.SignOff is not null));
    }

    static async Task<IResult> Record(Guid patientId, [FromBody] RecordGeriatricAssessment? body, [FromServices] IGeriatricAssessments service, [FromServices] IPatientDirectory patients,
        [FromServices] IStaffUsers users, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        if (body is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["assessment"] = ["empty"] });
        var row = await service.RecordAsync(patientId, facilityId, body, ct);
        return Results.Ok(row with { Author = (await users.DisplayNamesAsync([row.RecordedBy], ct)).GetValueOrDefault(row.RecordedBy) });
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
        catch (GeriatricValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
    }
}
