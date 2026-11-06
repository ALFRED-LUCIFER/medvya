using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Patients.Api;

public sealed record AccessRequestBody(AccessRequestReason Reason);

public sealed record GrantBody(int Days);

/// <summary>One pending request in the owner's queue: who asks, for which patient (name and MRN, so the owner can decide), and why.</summary>
public sealed record PendingAccessRequest(Guid Id, Guid PatientId, string PatientName, string Mrn, Guid RequestedBy, string? RequesterName,
    AccessRequestReason Reason, DateTimeOffset RequestedAt);

public static class AccessRequestEndpoints
{
    /// <summary>
    /// Request access (MDV-P2-024). A clinician refused for want of a care relationship asks for one patient at the facility they work at
    /// (the session's, never the body's); owners (Staff, roles, integrations: Config) see the facility's queue and grant for a fixed time
    /// or decline. Each step is audited in the transaction of its change, and the queue read like any read of patient names.
    /// </summary>
    public static IEndpointRouteBuilder MapAccessRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/patients/{patientId:guid}/access-requests", Request)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.access.request", "access_request", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);

        var owners = app.MapGroup("/v1/access-requests").RequireAuthorization(StaffPolicies.ManageStaff);
        owners.MapGet("/", Pending).Audited("patient.access.queue", "access_request", AuditPurpose.Operations).AddEndpointFilter(MapErrors);
        owners.MapPost("/{requestId:guid}/grant", async (Guid requestId, [FromBody] GrantBody? body, [FromServices] IAccessRequests requests, CancellationToken ct) =>
                await requests.GrantAsync(requestId, body?.Days ?? 0, ct) is { } r ? Results.Ok(r) : Results.NotFound())
            .AuditedByUseCase("patient.access.grant", "access_request", AuditPurpose.Operations).AddEndpointFilter(MapErrors);
        owners.MapPost("/{requestId:guid}/decline", async (Guid requestId, [FromServices] IAccessRequests requests, CancellationToken ct) =>
                await requests.DeclineAsync(requestId, ct) is { } r ? Results.Ok(r) : Results.NotFound())
            .AuditedByUseCase("patient.access.decline", "access_request", AuditPurpose.Operations).AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> Request(Guid patientId, [FromBody] AccessRequestBody? body, [FromServices] ICurrentFacility facility, [FromServices] IPatientDirectory patients,
        [FromServices] IAccessRequests requests, CancellationToken ct)
    {
        if (facility.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (body is null) return Invalid("reason", "required");
        // Only a patient of this tenant can be asked for; anything else is 404, audited NotFound by the filter.
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        return Results.Ok(await requests.RequestAsync(patientId, facilityId, body.Reason, ct));
    }

    static async Task<IResult> Pending([FromServices] ICurrentFacility facility, [FromServices] IAccessRequests requests, [FromServices] IPatientDirectory patients,
        [FromServices] IStaffUsers users, CancellationToken ct)
    {
        if (facility.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        var pending = await requests.PendingAsync(facilityId, ct);
        var people = await patients.SummariesAsync([.. pending.Select(r => r.PatientId)], ct);
        var names = await users.DisplayNamesAsync([.. pending.Select(r => r.RequestedBy).Distinct()], ct);
        return Results.Ok(new
        {
            requests = pending.Where(r => people.ContainsKey(r.PatientId)).Select(r => new PendingAccessRequest(
                r.Id, r.PatientId, people[r.PatientId].FullName, people[r.PatientId].Mrn, r.RequestedBy, names.GetValueOrDefault(r.RequestedBy), r.Reason, r.RequestedAt)),
            grantDays = ClinicalAccessRules.GrantDays,
        });
    }

    /// <summary>
    /// The 403 a clinical endpoint answers when <see cref="IClinicalAccess"/> refuses: code <c>no_care_relationship</c> when asking for
    /// access can help (and whether a request is already waiting), else <c>no_access</c>. Never says anything about the patient.
    /// </summary>
    public static IResult Refused(ClinicalAccessDeniedException e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No access to this patient",
            extensions: new Dictionary<string, object?> { ["code"] = e.CanRequest ? "no_care_relationship" : "no_access", ["requestPending"] = e.RequestPending });
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
            return Refused(e);
        }
        catch (SettingsValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (AccessRequestRejectedException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Request cannot be decided", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }

    static IResult Invalid(string field, string code) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [code] });
}
