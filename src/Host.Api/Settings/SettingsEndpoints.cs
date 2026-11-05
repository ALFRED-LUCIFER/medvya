using Host.Api.Auth;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Microsoft.AspNetCore.Mvc;

namespace Host.Api.Settings;

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

    public static WebApplication MapPracticeSettings(this WebApplication app)
    {
        var g = app.MapGroup("/v1/settings").RequireAuthorization(StaffPolicies.ManageStaff).AddEndpointFilter(MapErrors);

        g.MapGet("/profile", ([FromServices] IPracticeSettings s, CancellationToken ct) => s.GetProfileAsync(ct));
        g.MapPut("/profile", ([FromBody] SaveProfile body, [FromServices] IPracticeSettings s, CancellationToken ct) => s.SaveProfileAsync(body, ct));

        g.MapGet("/locations", ([FromServices] IPracticeSettings s, CancellationToken ct) => s.ListLocationsAsync(ct));
        g.MapPost("/locations", async ([FromBody] SaveLocation body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            Results.Created("/v1/settings/locations", await s.AddLocationAsync(body, ct)));
        g.MapPut("/locations/{id:guid}", async (Guid id, [FromBody] SaveLocation body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.UpdateLocationAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        g.MapGet("/locations/{id:guid}/hours", async (Guid id, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.GetHoursAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        g.MapPut("/locations/{id:guid}/hours", async (Guid id, [FromBody] IReadOnlyList<DayHours> body, [FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.SetHoursAsync(id, body, ct) is { } v ? Results.Ok(v) : Results.NotFound());

        // Holidays: each lists the booked visits still on that date, which the desk has to move (new bookings are refused by the booking path).
        g.MapGet("/locations/{id:guid}/closures", async (Guid id, [FromServices] IPracticeSettings s, [FromServices] IScheduleService schedule, CancellationToken ct) =>
        {
            if (await s.ListClosuresAsync(id, ct) is not { } closures) return Results.NotFound();
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)); // UTC-1 day covers every facility zone; past closures need no list
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
        });
        g.MapDelete("/logo", async ([FromServices] IPracticeSettings s, CancellationToken ct) => await s.RemoveLogoAsync(ct) ? Results.NoContent() : Results.NotFound());
        g.MapGet("/logo", async ([FromServices] IPracticeSettings s, CancellationToken ct) =>
            await s.LogoLinkAsync(LogoLinkLifetime, ct) is { } url ? Results.Ok(new LogoLink(url, DateTimeOffset.UtcNow + LogoLinkLifetime)) : Results.NotFound());

        // Terms of Service and DPA (MDV-P1-019): anyone who administers the practice can see the status; only an owner (practice admin or
        // solo doctor) can accept, which the service checks. Until accepted, booking and sending are refused (RequireAcceptedTerms).
        g.MapGet("/terms", async ([FromServices] ITermsAcceptance terms, CancellationToken ct) => Results.Ok(await terms.StatusAsync(ct)));
        g.MapPost("/terms/accept", async ([FromServices] ITermsAcceptance terms, CancellationToken ct) =>
            await terms.AcceptAsync(ct) ? Results.Ok(await terms.StatusAsync(ct)) : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the practice owner can accept"));

        return app;
    }

    /// <summary>Use-case errors as problem details: field/code pairs for 400, a generic 403 that never says why.</summary>
    internal static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SettingsValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(x => x.Key, x => x.Select(y => y.Code).ToArray()));
        }
        catch (SchedulingAccessDeniedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No access at this facility");
        }
        catch (InvitationEmailException)
        {
            return EmailFailed;
        }
    }

    /// <summary>502 with a stable code: the screen says the invitation email could not be sent and nothing was saved, not a generic failure.</summary>
    internal static IResult EmailFailed => Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The invitation email could not be sent.", extensions: new Dictionary<string, object?> { ["code"] = "email_failed" });
}
