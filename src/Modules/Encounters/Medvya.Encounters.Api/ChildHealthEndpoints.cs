using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

public static class ChildHealthEndpoints
{
    /// <summary>
    /// Immunisations and milestones (MDV-P2-036, E-01) at the session's facility. Reading: a role with the Patient 360 capability; recording:
    /// vitals entry at Full (doctor or nurse). The use case then checks the care relationship and audits with the patient. Only the patient id
    /// is in a path; the vaccine, batch, reason, answer and measurements travel in the body. Growth (MDV-P2-035) follows the same rules.
    /// </summary>
    public static IEndpointRouteBuilder MapChildHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/patients/{patientId:guid}/child-health", (Guid patientId, [FromServices] IChildHealth service, [FromServices] ICurrentFacility current, [FromServices] IPatientDirectory patients, CancellationToken ct) =>
                Run(current, patients, patientId, f => service.GetAsync(patientId, f, ct), ct))
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("childhealth.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);
        app.MapPost("/v1/patients/{patientId:guid}/immunisations", (Guid patientId, [FromBody] RecordDose? body, [FromServices] IChildHealth service, [FromServices] ICurrentFacility current, [FromServices] IPatientDirectory patients, CancellationToken ct) =>
                Run(current, patients, patientId, f => service.RecordDoseAsync(patientId, f, body ?? new RecordDose(null, null, null, null, null, null, null), ct), ct))
            .RequireCapability(Capability.VitalsAllergiesEntry, Access.Full)
            .AuditedByUseCase("immunisation.record", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);
        app.MapPost("/v1/patients/{patientId:guid}/milestones", (Guid patientId, [FromBody] AnswerMilestone? body, [FromServices] IChildHealth service, [FromServices] ICurrentFacility current, [FromServices] IPatientDirectory patients, CancellationToken ct) =>
                Run(current, patients, patientId, f => service.AnswerMilestoneAsync(patientId, f, body ?? new AnswerMilestone(null, null, null, null), ct), ct))
            .RequireCapability(Capability.VitalsAllergiesEntry, Access.Full)
            .AuditedByUseCase("milestone.answer", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);
        app.MapGet("/v1/patients/{patientId:guid}/growth", (Guid patientId, [FromServices] IChildHealth service, [FromServices] ICurrentFacility current, [FromServices] IPatientDirectory patients, CancellationToken ct) =>
                Run(current, patients, patientId, f => service.GrowthAsync(patientId, f, ct), ct))
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("growth.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);
        app.MapPost("/v1/patients/{patientId:guid}/growth", (Guid patientId, [FromBody] RecordGrowth? body, [FromServices] IChildHealth service, [FromServices] ICurrentFacility current, [FromServices] IPatientDirectory patients, CancellationToken ct) =>
                Run(current, patients, patientId, f => service.RecordGrowthAsync(patientId, f, body ?? new RecordGrowth(null, null, null, null, null), ct), ct))
            .RequireCapability(Capability.VitalsAllergiesEntry, Access.Full)
            .AuditedByUseCase("growth.record", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> Run<T>(ICurrentFacility current, IPatientDirectory patients, Guid patientId, Func<Guid, Task<T>> work, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        // Existence only comes before the access check; nothing about the patient is returned until it passes.
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        return Results.Ok(await work(facilityId));
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
        catch (ChildHealthValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (ChildHealthConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This cannot be recorded", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
