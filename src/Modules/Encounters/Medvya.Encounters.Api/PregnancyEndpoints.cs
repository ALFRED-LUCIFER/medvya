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

public static class PregnancyEndpoints
{
    /// <summary>
    /// The pregnancy record (MDV-P2-034). Starting, re-dating and closing: a doctor (clinical notes at Sign). Danger-sign counselling, "not
    /// pregnant" and the antenatal readings: a doctor or a nurse (vitals and allergies entry at Full). The use case then checks clinical access
    /// to the patient at the session's facility and audits with the patient. Dates and counts are in the body, never the URL.
    /// </summary>
    public static IEndpointRouteBuilder MapPregnancyEndpoints(this IEndpointRouteBuilder app)
    {
        const string path = "/v1/patients/{patientId:guid}/pregnancy";
        app.MapPost(path, (Guid patientId, [FromBody] StartPregnancy? body, [FromServices] IPregnancies s, [FromServices] IPatientDirectory p, [FromServices] ICurrentFacility f, CancellationToken ct) =>
                Run(patientId, p, f, facility => s.StartAsync(patientId, facility, body ?? new StartPregnancy(null, null, null, null, null, null, null, null), ct), ct))
            .Doctor("pregnancy.start");
        app.MapPut($"{path}/dating", (Guid patientId, [FromBody] PregnancyDates? body, [FromServices] IPregnancies s, [FromServices] IPatientDirectory p, [FromServices] ICurrentFacility f, CancellationToken ct) =>
                Run(patientId, p, f, facility => s.DateAsync(patientId, facility, body ?? new PregnancyDates(null, null, null, null), ct), ct))
            .Doctor("pregnancy.date");
        app.MapPost($"{path}/close", (Guid patientId, [FromBody] ClosePregnancy? body, [FromServices] IPregnancies s, [FromServices] IPatientDirectory p, [FromServices] ICurrentFacility f, CancellationToken ct) =>
                Run(patientId, p, f, facility => s.CloseAsync(patientId, facility, body ?? new ClosePregnancy(null), ct), ct))
            .Doctor("pregnancy.close");
        app.MapPost($"{path}/danger-signs", (Guid patientId, [FromServices] IPregnancies s, [FromServices] IPatientDirectory p, [FromServices] ICurrentFacility f, CancellationToken ct) =>
                Run(patientId, p, f, facility => s.CounselDangerSignsAsync(patientId, facility, ct), ct))
            .Clinician("pregnancy.danger-signs");
        app.MapPost($"{path}/ruled-out", (Guid patientId, [FromServices] IPregnancies s, [FromServices] IPatientDirectory p, [FromServices] ICurrentFacility f, CancellationToken ct) =>
                Run(patientId, p, f, facility => s.RuleOutAsync(patientId, facility, ct), ct))
            .Clinician("pregnancy.ruled-out");
        app.MapPost($"{path}/readings", (Guid patientId, [FromBody] PregnancyReadings? body, [FromServices] IPregnancies s, [FromServices] IPatientDirectory p, [FromServices] ICurrentFacility f, CancellationToken ct) =>
                Run(patientId, p, f, facility => s.RecordReadingsAsync(patientId, facility, body ?? new PregnancyReadings(null, null), ct), ct))
            .Clinician("pregnancy.readings");
        return app;
    }

    static RouteHandlerBuilder Doctor(this RouteHandlerBuilder b, string action) =>
        b.RequireCapability(Capability.ClinicalNotes, Access.Sign).AuditedByUseCase(action, "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);

    static RouteHandlerBuilder Clinician(this RouteHandlerBuilder b, string action) =>
        b.RequireCapability(Capability.VitalsAllergiesEntry, Access.Full).AuditedByUseCase(action, "patient", AuditPurpose.Treatment, patientRouteKey: "patientId").AddEndpointFilter(MapErrors);

    static async Task<IResult> Run(Guid patientId, IPatientDirectory patients, ICurrentFacility current, Func<Guid, Task<PregnancyStatus>> work, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
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
        catch (PregnancyValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (PregnancyConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The pregnancy record cannot do this now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
