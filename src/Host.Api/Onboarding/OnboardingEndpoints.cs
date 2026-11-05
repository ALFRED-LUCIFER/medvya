using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Host.Api.Context;
using Medvya.Identity.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Host.Api.Onboarding;

/// <summary>
/// The sign-up wizard (F-04, MDV-P1-004). Everything here is for the signed-in person's own draft: no tenant exists until
/// "finish", so these endpoints need a session but no facility. Server-side validation is the only validation that counts;
/// the page repeats the required-field checks only to answer faster.
/// </summary>
public static partial class OnboardingEndpoints
{
    public sealed record StartRequest(string? Country);
    public sealed record SaveRequest(int Step, int? Validate, OnboardingData? Data);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static WebApplication MapOnboarding(this WebApplication app)
    {
        var group = app.MapGroup("/v1/onboarding").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, [FromServices] IOnboardingDrafts drafts, CancellationToken ct) =>
            await drafts.GetAsync(UserId(principal), ct) is { } draft ? Results.Ok(View(draft.Country, draft.Step, draft.Data)) : Results.NoContent());

        group.MapPost("/", async (StartRequest request, ClaimsPrincipal principal, IConfiguration config, [FromServices] IOnboardingDrafts drafts, CancellationToken ct) =>
        {
            if (OnboardingRules.For(request.Country) is null) return Results.ValidationProblem(Errors([new("country", "unknown")]));
            // One deployment serves one country (ADR-003): a tenant is created only in its own country's data plane.
            if (config["Onboarding:DeploymentCountry"] is { Length: > 0 } live && live != request.Country)
                return Results.ValidationProblem(Errors([new("country", "otherDeployment")]));
            var draft = await drafts.StartAsync(UserId(principal), request.Country!, ct);
            return Results.Ok(View(draft.Country, draft.Step, draft.Data));
        });

        // Autosave. With `validate` the named step is checked first and a failed check saves nothing; without it the typed values
        // are kept as they are (a half-finished step), and the same checks run again at Continue and at finish.
        group.MapPut("/", async (SaveRequest request, ClaimsPrincipal principal, [FromServices] IOnboardingDrafts drafts, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            if (request.Data is null || request.Step is < 1 or > 6) return Results.ValidationProblem(Errors([new("step", "unknown")]));
            var draft = await drafts.GetAsync(userId, ct);
            if (draft is null) return Results.NotFound();
            if (request.Validate is { } step && OnboardingRules.For(draft.Country)!.ValidateStep(step, request.Data) is { Count: > 0 } errors)
                return Results.ValidationProblem(Errors(errors));
            return await drafts.SaveAsync(userId, request.Step, JsonSerializer.Serialize(request.Data, Json), ct) ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/finish", async (HttpContext http, ClaimsPrincipal principal, [FromServices] IOnboardingDrafts drafts, [FromServices] IPracticeProvisioning provisioning,
            [FromServices] IStaffUsers users, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            if (await drafts.GetAsync(userId, ct) is not { } draft) return Results.NotFound();
            var data = JsonSerializer.Deserialize<OnboardingData>(draft.Data, Json) ?? new OnboardingData(null, null, null, null, null, null);
            if (OnboardingRules.For(draft.Country)!.ValidateAll(data) is { Count: > 0 } errors) return Results.ValidationProblem(Errors(errors));

            // shortcut: two finishes at the same moment would create two practices (the page disables the button while saving);
            // claim the draft in the same transaction as the tenant once the drafts move into one store with it.
            var practice = await provisioning.CreateAsync(draft.Country, data, ct);
            await drafts.CompleteAsync(userId, ct);
            await users.SetDisplayNameAsync(userId, data.Profile!.FullName!, ct);

            // Step 6 goes through the invitation flow (MDV-P0-013), run inside the new tenant so its audit rows land there.
            int sent = 0, failed = 0;
            await using var scope = http.RequestServices.EnterLinkTenant(practice.TenantId);
            // The owner doctor is bookable straight away: the hours and slot length of step 4 become their weekly sessions (an upsert, so a repeat adds none).
            if (practice.Role == Role.SoloDoctor)
            {
                try
                {
                    await scope.ServiceProvider.GetRequiredService<IScheduleService>().SaveTemplateAsync(
                        new SaveTemplate { FacilityId = practice.FacilityId, CalendarId = userId, CalendarKind = CalendarKind.Doctor, SlotMinutes = data.Hours!.SlotMinutes!.Value, Hours = Sessions(data.Hours) }, ct);
                }
                // The practice exists already; hours the schedule rules reject (e.g. a time off the 5-minute grid) are set again in Settings, Hours.
                catch (SchedulingValidationException) { OwnerSessionsRejected(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(OnboardingEndpoints))); }
            }
            var invitations = scope.ServiceProvider.GetRequiredService<IInvitations>();
            foreach (var member in data.Team ?? [])
            {
                try
                {
                    if (await invitations.CreateAsync(member.Email!.Trim(), member.Role!.Value, practice.FacilityId, cancellationToken: ct) is not null) sent++; else failed++;
                }
                // The practice exists already; an invitation that could not go out is repeated from the staff screen, so it is counted, not thrown.
                catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException or DbUpdateException) { failed++; }
            }
            return Results.Ok(new { practice.MembershipId, Role = practice.Role.ToString(), InvitationsSent = sent, InvitationsFailed = failed });
        });

        return app;
    }

    // Fixed message, no arguments: nothing about the practice or the hours is logged.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Onboarding finished without the owner's bookable sessions: the schedule rules rejected the hours")]
    static partial void OwnerSessionsRejected(ILogger logger);

    static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    static List<WorkingHours> Sessions(OnboardingHours hours) =>
    [
        .. Weekdays.SelectMany(day => (hours.Weekdays ?? []).Select(w => Session(day, w))),
        .. (hours.Saturday ?? []).Select(w => Session(DayOfWeek.Saturday, w)),
    ];

    static WorkingHours Session(DayOfWeek day, HoursWindow w) =>
        new() { Weekday = day, Start = TimeOnly.ParseExact(w.Start!, "HH:mm", CultureInfo.InvariantCulture), End = TimeOnly.ParseExact(w.End!, "HH:mm", CultureInfo.InvariantCulture) };

    static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    static Dictionary<string, string[]> Errors(IEnumerable<OnboardingError> errors) =>
        errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray());

    // The draft plus what the page needs to render the country's step 2/4/5 choices; the server stays the authority on them.
    static object View(string country, int step, string data)
    {
        var rules = OnboardingRules.For(country)!;
        return new
        {
            country, step, data = JsonSerializer.Deserialize<JsonElement>(data),
            rules = new { rules.Currency, authorities = rules.Authorities, slotMinutes = OnboardingRules.SlotMinutes, templates = OnboardingRules.TemplateIds },
        };
    }
}
