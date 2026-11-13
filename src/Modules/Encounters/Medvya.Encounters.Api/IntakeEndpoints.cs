using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
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

/// <summary>The intake with who it is for (identity check at the start of N-01) and the facility's time zone for dates.</summary>
public sealed record IntakeScreen(IntakeView Intake, BannerPatient Patient, string TimeZone);

public static class IntakeEndpoints
{
    /// <summary>
    /// Nurse intake (MDV-P2-007). Signed in with "Vitals and allergies entry" at Full; the use case then checks clinical access to the patient at the visit's facility and writes the audit event with the patient.
    /// Ids only in paths; nothing about the patient in a URL. The read, vitals and allergy routes also serve the doctor's consultation entry (audit P0-2),
    /// so they work with the practice's nurse-intake switch off; the nurse-only escalate, medicines and handoff routes then answer 404 (MDV-P0-028).
    /// </summary>
    public static IEndpointRouteBuilder MapIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var intake = app.MapGroup("/v1/intake").RequireCapability(Capability.VitalsAllergiesEntry, Access.Full);

        intake.MapGet("/{appointmentId:guid}", Screen).AuditedByUseCase("intake.read", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        intake.MapPut("/{appointmentId:guid}", async (Guid appointmentId, [FromBody] RecordIntake? body, [FromServices] IIntake service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["required"] })
                : await service.RecordAsync(appointmentId, body, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.record", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        // The kind is parsed here, inside the audit filter, so an unknown one is an audited rejection rather than a binding failure with no row.
        intake.MapPost("/{appointmentId:guid}/vitals/{kind}/keep", async (Guid appointmentId, string kind, [FromServices] IIntake service, CancellationToken ct) =>
                !Enum.TryParse<VitalKind>(kind, out var parsed) || !Enum.IsDefined(parsed) ? Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["invalid"] })
                : await service.KeepAsync(appointmentId, parsed, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.keep", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        intake.MapPost("/{appointmentId:guid}/escalate", async (Guid appointmentId, [FromBody] EscalateIntake? body, [FromServices] IIntake service, CancellationToken ct) =>
                await service.EscalateAsync(appointmentId, body ?? new EscalateIntake(null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.escalate", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors).RequireFeature(IntakeFlag.Key);
        // Allergies and patient-reported medicines (MDV-P2-008). The handoff below is impossible until every allergy has a status.
        intake.MapPost("/{appointmentId:guid}/allergies", async (Guid appointmentId, [FromBody] AddAllergy? body, [FromServices] IIntake service, CancellationToken ct) =>
                await service.AddAllergyAsync(appointmentId, body ?? new AddAllergy(null, null, null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.allergy.add", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        intake.MapPut("/{appointmentId:guid}/allergies/{allergyId:guid}/status", async (Guid appointmentId, Guid allergyId, [FromBody] SetAllergyStatus? body, [FromServices] IIntake service, CancellationToken ct) =>
                await service.SetAllergyStatusAsync(appointmentId, allergyId, body ?? new SetAllergyStatus(null), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.allergy.status", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        intake.MapPost("/{appointmentId:guid}/no-known-allergies", async (Guid appointmentId, [FromServices] IIntake service, CancellationToken ct) =>
                await service.ConfirmNoKnownAllergiesAsync(appointmentId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.allergy.none", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        intake.MapPost("/{appointmentId:guid}/medicines", async (Guid appointmentId, [FromBody] AddMedicine? body, [FromServices] IIntake service, CancellationToken ct) =>
                await service.AddMedicineAsync(appointmentId, body ?? new AddMedicine(null, null, null, false), ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.medicine.add", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors).RequireFeature(IntakeFlag.Key);
        intake.MapPost("/{appointmentId:guid}/handoff", async (Guid appointmentId, [FromServices] IIntake service, CancellationToken ct) =>
                await service.HandOffAsync(appointmentId, ct) is { } view ? Results.Ok(view) : Results.NotFound())
            .AuditedByUseCase("intake.handoff", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors).RequireFeature(IntakeFlag.Key);

        // The treating doctor's urgent items (feeds Today, MDV-P2-002): a doctor's capability, not the nurse's.
        app.MapGroup("/v1/intake-escalations").RequireCapability(Capability.Patient360Chart, Access.Full)
            .MapGet("/", async (Guid? facilityId, [FromServices] IIntake service, CancellationToken ct) =>
                facilityId is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["facilityId"] = ["required"] }) : Results.Ok(await service.EscalationsAsync(facilityId.Value, ct)))
            .AuditedByUseCase("intake.escalations.read", "intake", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);
        return app;
    }

    static async Task<IResult> Screen(Guid appointmentId, [FromServices] IIntake service, [FromServices] IPatientDirectory patients,
        [FromServices] IStaffAccess staff, [FromServices] IFacilityDirectory facilities, [FromServices] TimeProvider clock, CancellationToken ct)
    {
        if (await service.GetAsync(appointmentId, ct) is not { } view) return Results.NotFound();
        if ((await patients.SummariesAsync([view.PatientId], ct)).GetValueOrDefault(view.PatientId) is not { } p) return Results.NotFound();
        var today = CountryRules.For(await staff.TenantCountryAsync(ct) ?? throw new InvalidOperationException("No tenant country.")).Today(clock.GetUtcNow());
        return Results.Ok(new IntakeScreen(view, EncounterEndpoints.BannerOf(p, today),
            (await facilities.FindAsync(view.FacilityId, ct) ?? throw new InvalidOperationException("Visit facility not in this tenant.")).TimeZone));
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
        catch (IntakeValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (IntakeConflictException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Intake cannot do this now",
                extensions: new Dictionary<string, object?> { ["code"] = e.Code, ["blockers"] = e.Blockers });
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
