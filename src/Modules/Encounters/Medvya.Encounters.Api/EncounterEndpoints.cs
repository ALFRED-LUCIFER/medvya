using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

/// <summary>Who the encounter is for, as the identity banner shows it on every clinical screen (MDV-P2-001).</summary>
public sealed record BannerPatient(Guid Id, string FullName, string Mrn, DateOnly DateOfBirth, int AgeYears, SexAtBirth SexAtBirth);

/// <summary>
/// The encounter with its patient's identity and treatment consent. Allergies are not here yet: they are recorded from MDV-P2-005, and until
/// then the banner says they are not recorded (never "none"). Times show in <see cref="TimeZone"/>, the encounter facility's zone.
/// </summary>
public sealed record EncounterBanner(EncounterView Encounter, BannerPatient Patient, bool TreatmentConsent, string TimeZone);

public static class EncounterEndpoints
{
    /// <summary>
    /// Encounters (MDV-P2-001). Signed in with a role that treats patients at the session's facility; the use case then checks clinical
    /// access to the patient at the visit's facility (role, care relationship, MDV-P2-024) and writes the audit event with the patient.
    /// A refusal, a missing encounter or a conflict is audited here. Ids only in paths; nothing about the patient in a URL.
    /// </summary>
    public static IEndpointRouteBuilder MapEncounterEndpoints(this IEndpointRouteBuilder app)
    {
        var encounters = app.MapGroup("/v1/encounters").RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign);

        encounters.MapPost("/", async ([FromBody] StartEncounter? body, [FromServices] IEncounters service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["appointmentId"] = ["required"] })
                : await service.StartAsync(body, ct) is { } started ? Results.Ok(started) : Results.NotFound())
            .AuditedByUseCase("encounter.start", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        // Opening the encounter shows who it is for: audited by the use case with the patient, like opening a record.
        encounters.MapGet("/{encounterId:guid}", Banner).AuditedByUseCase("encounter.read", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        encounters.MapPost("/{encounterId:guid}/end", async (Guid encounterId, [FromServices] IEncounters service, CancellationToken ct) =>
                await service.EndAsync(encounterId, ct) is { } ended ? Results.Ok(ended) : Results.NotFound())
            .AuditedByUseCase("encounter.end", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> Banner(Guid encounterId, [FromServices] IEncounters service, [FromServices] IPatientDirectory patients, [FromServices] IPatientConsents consents,
        [FromServices] IStaffAccess staff, [FromServices] IFacilityDirectory facilities, [FromServices] TimeProvider clock, CancellationToken ct)
    {
        if (await service.GetAsync(encounterId, ct) is not { } encounter) return Results.NotFound();
        if ((await patients.SummariesAsync([encounter.PatientId], ct)).GetValueOrDefault(encounter.PatientId) is not { } p) return Results.NotFound();
        var today = CountryRules.For(await staff.TenantCountryAsync(ct) ?? throw new InvalidOperationException("No tenant country.")).Today(clock.GetUtcNow());
        return Results.Ok(new EncounterBanner(encounter, new BannerPatient(p.Id, p.FullName, p.Mrn, p.DateOfBirth, CountryRules.AgeOn(today, p.DateOfBirth), p.SexAtBirth),
            await consents.IsGrantedAsync(p.Id, ConsentType.Treatment, cancellationToken: ct),
            (await facilities.FindAsync(encounter.FacilityId, ct) ?? throw new InvalidOperationException("Encounter facility not in this tenant.")).TimeZone));
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
        catch (OpenEncounterException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another consultation is open",
                extensions: new Dictionary<string, object?> { ["code"] = "open_encounter", ["encounterId"] = e.EncounterId });
        }
        catch (EncounterEndedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This consultation has ended", extensions: new Dictionary<string, object?> { ["code"] = "ended" });
        }
        catch (VisitTransitionException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Visit cannot make this move", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
        catch (AppointmentNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
