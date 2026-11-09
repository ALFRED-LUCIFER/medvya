using Medvya.Audit.Api;
using Medvya.BuildingBlocks.Domain;
using Medvya.Audit.Domain;
using Medvya.Prescribing.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Prescribing.Api;

public static class ReferralEndpoints
{
    /// <summary>
    /// Referrals out and in (MDV-P2-019). Writing and the letter need "Prescriptions and orders" at Sign, then clinical access to the patient in the use case,
    /// which writes the audit event with the patient. The Patient 360 list needs the chart's capability. Ids only in paths; names, reasons and notes travel in bodies.
    /// </summary>
    public static IEndpointRouteBuilder MapReferralEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1").RequireCapability(Capability.PrescriptionsOrders, Access.Sign);

        g.MapPost("/encounters/{encounterId:guid}/referrals", async (Guid encounterId, [FromBody] CreateReferral? body, [FromServices] IReferrals service, CancellationToken ct) =>
                await service.CreateAsync(encounterId, body ?? new CreateReferral(null, null, null, null), ct) is { } v ? Results.Created($"/v1/referrals/{v.Id}", v) : Results.NotFound())
            .AuditedByUseCase("referral.create", "referral", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/referrals/in", async ([FromBody] FileReferralIn? body, [FromServices] IReferrals service, [FromServices] ICurrentFacility current, CancellationToken ct) =>
                current.FacilityId is not { } facilityId ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context")
                : await service.FileInAsync(facilityId, body ?? new FileReferralIn(null, null, null, null), ct) is { } v ? Results.Created($"/v1/referrals/{v.Id}", v) : Results.NotFound())
            .AuditedByUseCase("referral.file_in", "referral", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/referrals/{referralId:guid}/send", async (Guid referralId, [FromBody] SendReferral? body, [FromServices] IReferrals service, CancellationToken ct) =>
                await service.SendAsync(referralId, body ?? new SendReferral(null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("referral.send", "referral", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapPost("/referrals/{referralId:guid}/status", async (Guid referralId, [FromBody] AdvanceReferral? body, [FromServices] IReferrals service, CancellationToken ct) =>
                await service.AdvanceAsync(referralId, body ?? new AdvanceReferral(null, null, null), ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("referral.advance", "referral", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        g.MapGet("/referrals/{referralId:guid}/letter", async (Guid referralId, [FromServices] IReferrals service, CancellationToken ct) =>
                await service.LetterAsync(referralId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("referral.letter", "referral", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        // Patient 360 shows the open referrals to every role that reads the chart (a nurse too), so this is the chart's capability, not the doctor's.
        app.MapGroup("/v1/patients").RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .MapGet("/{patientId:guid}/referrals", async (Guid patientId, [FromServices] IReferrals service, [FromServices] ICurrentFacility current, CancellationToken ct) =>
                current.FacilityId is not { } facilityId ? Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context")
                : await service.ForPatientAsync(patientId, facilityId, ct) is { } v ? Results.Ok(v) : Results.NotFound())
            .AuditedByUseCase("referral.read", "referral", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
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
        catch (ReferralValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (ReferralConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The referral cannot do this now", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }
}
