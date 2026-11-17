using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Api;

/// <summary>
/// Practice settings (F-08, MDV-P1-005): profile, locations, opening hours, holidays and the logo. Every endpoint needs the
/// practice-configuration capability (<see cref="StaffPolicies.ManageStaff"/>: practice admin, owner doctor, hospital IT), so any
/// other role gets 403 from the server whatever the screen shows. Changes are audited inside the service with before/after.
/// None of it holds patient data; the holiday list carries visit references and times only.
/// </summary>
public static class SettingsEndpoints
{
    /// <summary>A signed logo link is good for this long; the screen asks again when it needs one.</summary>
    static readonly TimeSpan LogoLinkLifetime = TimeSpan.FromMinutes(5);

    public sealed record ClosureBody(DateOnly Date, string? Name);
    public sealed record ClosureWithVisits(ClosureView Closure, IReadOnlyList<AppointmentToMove> AppointmentsToMove);
    public sealed record LogoLink(Uri Url, DateTimeOffset ExpiresAt);
    public sealed record ClinicalAccessBody(ClinicalAccessPolicy Policy);
    public sealed record NurseIntakeBody(bool Enabled);
    /// <summary>The switch and how many patients are with the nurse now, which the owner is told before switching (MDV-P2-055).</summary>
    public sealed record NurseIntakeView(bool Enabled, int WithNurse);

    /// <summary>
    /// A setting of the whole organisation (legal profile printed on prescriptions, logo, a new location, queue rule, nurse intake) needs an
    /// owner or an administrator of every location, as the clinical-access policy needs the owner: an administrator of one branch changes only that branch (#1417).
    /// SAFETY: mutation-checked in PracticeSettingsTests.
    /// </summary>
    static async ValueTask<object?> OrganisationWide(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        await context.HttpContext.RequestServices.GetRequiredService<IPracticeSettings>().AdministersEveryLocationAsync(context.HttpContext.RequestAborted)
            ? await next(context)
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only an administrator of every location changes this", extensions: new Dictionary<string, object?> { ["code"] = "organisation_admin_required" });

    public static WebApplication MapPracticeSettings(this WebApplication app)
    {
        var g = app.MapGroup("/v1/settings").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(MapErrors);

        g.MapGet("/profile", ([FromServices] IPracticeSettings s, CancellationToken ct) => s.GetProfileAsync(ct));
        // The version is required: a save always says which version it read, so it never overwrites a change it has not seen (#889).
        g.MapPut("/profile", async Task<Results<ValidationProblem, Ok<ProfileView>>> ([FromBody] SaveProfile body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            body.Version is null ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["required"] }) : TypedResults.Ok(await s.SaveProfileAsync(body, ct))).AddEndpointFilter(OrganisationWide);

        g.MapGet("/locations", ([FromServices] IPracticeSettings s, CancellationToken ct) => s.ListLocationsAsync(ct));
        g.MapPost("/locations", async ([FromBody] AddLocation body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            Results.Created("/v1/settings/locations", await s.AddLocationAsync(body, ct))).AddEndpointFilter(OrganisationWide);
        g.MapPut("/locations/{id:guid}", async (Guid id, [FromBody] SaveLocation body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.UpdateLocationAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        g.MapGet("/locations/{id:guid}/hours", async (Guid id, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.GetHoursAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        g.MapPut("/locations/{id:guid}/hours", async (Guid id, [FromBody] IReadOnlyList<DayHours> body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.SetHoursAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // Holidays: each lists the booked visits still on that date, which the desk has to move (new bookings are refused by the booking path).
        g.MapGet("/locations/{id:guid}/closures", async (Guid id, [FromServices] IPracticeSettings s, [FromServices] IScheduleService schedule, [FromServices] TimeProvider clock, CancellationToken ct) =>
        {
            if (await s.ListClosuresAsync(id, ct) is not { } closures) return Results.NotFound();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(-1)); // UTC-1 day covers every facility zone; past closures need no list
            var rows = new List<ClosureWithVisits>();
            foreach (var c in closures) rows.Add(new(c, c.Date < today ? [] : await schedule.AppointmentsOnAsync(id, c.Date, ct)));
            return Results.Ok(rows);
        });
        g.MapPost("/locations/{id:guid}/closures", async (Guid id, [FromBody] ClosureBody body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.AddClosureAsync(new AddClosure { FacilityId = id, Date = body.Date, Name = body.Name }, ct) is { } v ? Results.Created($"/v1/settings/locations/{id}/closures", v) : Results.NotFound());
        g.MapDelete("/closures/{id:guid}", async (Guid id, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.RemoveClosureAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // The logo body is the image itself (PNG or JPEG, up to 512 KB), so no multipart parser sits in front of the type check.
        g.MapPut("/logo", async (HttpRequest request, [FromServices] IPracticeSettings s, CancellationToken ct) =>
        {
            if (request.ContentLength > LogoImage.MaxBytes) return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge);
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);
            await s.SetLogoAsync(buffer.ToArray(), ct);
            return Results.NoContent();
        }).AddEndpointFilter(OrganisationWide);
        g.MapDelete("/logo", async ([FromServices] IPracticeSettings s, CancellationToken ct) => await s.RemoveLogoAsync(ct) ? Results.NoContent() : Results.NotFound()).AddEndpointFilter(OrganisationWide);
        g.MapGet("/logo", async ([FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.LogoLinkAsync(LogoLinkLifetime, ct) is { } url ? Results.Ok(new LogoLink(url, DateTimeOffset.UtcNow + LogoLinkLifetime)) : Results.NotFound()); // wall clock: when the storage service stops honouring the link

        // Which clinicians treat the facility's patients (MDV-P2-024): every clinician of it (default) or only those assigned.
        g.MapGet("/clinical-access", async ([FromServices] IPracticeSettings s, CancellationToken ct) => Results.Ok(new ClinicalAccessBody(await s.GetClinicalAccessAsync(ct))));
        g.MapPut("/clinical-access", async ([FromBody] ClinicalAccessBody body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.SaveClinicalAccessAsync(body.Policy, ct) is { } saved ? Results.Ok(new ClinicalAccessBody(saved)) : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the practice owner can change this"));

        // The order arrived patients are seen in (MDV-P1-103): the doctor's day, the board and the queue display all follow it.
        g.MapGet("/queue-rule", ([FromServices] IPracticeSettings s, CancellationToken ct) => s.GetQueueRuleAsync(ct));
        g.MapPut("/queue-rule", ([FromBody] QueueRule body, [FromServices] IPracticeSettings s, CancellationToken ct) => s.SaveQueueRuleAsync(body, ct)).AddEndpointFilter(OrganisationWide);

        // Nurse intake before the doctor (MDV-P2-007): a practice-wide switch, off until the owner turns it on. Changes are audited by the flag service.
        // The switch exists only while Medvya's release reaches the practice (MDV-P5-041): otherwise 404, and the screen says it is not available.
        g.MapGet("/nurse-intake", async ([FromServices] IFeatureFlags flags, [FromServices] IScheduleService schedule, CancellationToken ct) =>
            Results.Ok(new NurseIntakeView(await flags.IsEnabledAsync(Features.NurseIntake, ct), await schedule.CountWithNurseAsync(ct)))).RequireRelease(Features.NurseIntake);
        g.MapPut("/nurse-intake", async ([FromBody] NurseIntakeBody body, [FromServices] IFeatureFlags flags, [FromServices] IScheduleService schedule, CancellationToken ct) =>
        {
            await flags.SetAsync(Features.NurseIntake, body.Enabled, "practice", "Nurse intake before the doctor (MDV-P2-007)", ct);
            return Results.Ok(new NurseIntakeView(body.Enabled, await schedule.CountWithNurseAsync(ct)));
        }).RequireRelease(Features.NurseIntake).AddEndpointFilter(OrganisationWide);

        // Terms of Service and DPA (MDV-P1-019): anyone who administers the practice can see the status; only an owner (practice admin or
        // owner-doctor) can accept, which the service checks. Until accepted, booking and sending are refused (RequireAcceptedTerms).
        g.MapGet("/terms", async ([FromServices] ITermsAcceptance terms, CancellationToken ct) => Results.Ok(await terms.StatusAsync(ct)));
        g.MapPost("/terms/accept", async ([FromServices] ITermsAcceptance terms, CancellationToken ct) =>
            await terms.AcceptAsync(ct) ? Results.Ok(await terms.StatusAsync(ct)) : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the practice owner can accept"));

        return app;
    }

    /// <summary>
    /// <see cref="SettingsProblems.MapErrors"/>, plus what only these settings raise: a scheduling refusal (moving visits off a holiday) as
    /// a generic 403 that never says why, and a concurrent save as 409.
    /// </summary>
    public static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await SettingsProblems.MapErrors(context, next);
        }
        catch (SchedulingAccessDeniedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No access at this facility");
        }
        catch (DbUpdateConcurrencyException)
        {
            // #889: another administrator saved the same settings meanwhile; nothing was saved.
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "These settings were changed by someone else. Reload and try again.",
                extensions: new Dictionary<string, object?> { ["code"] = "changed" });
        }
    }
}
