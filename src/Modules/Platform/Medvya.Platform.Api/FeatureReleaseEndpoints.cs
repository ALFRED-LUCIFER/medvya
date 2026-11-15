using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Platform.Api;

/// <summary>
/// The console's Features page (MDV-P5-041): Medvya's release of each listed feature per country, written to that country's database through the narrow
/// login medvya_features (<see cref="IFeatureReleaseConsole"/>). Every console role reads; only owner and operations change a release, each change with a
/// reason in the platform audit log. Turning a feature off hides it (gated endpoints answer 404); no clinic data is touched.
/// </summary>
public static class FeatureReleaseEndpoints
{
    public sealed record ReleaseBody(ReleaseStage Stage, int? Percentage, Guid[]? Organisations, string? ClinicalSignOff, string? Reason) : IReasoned;

    public sealed record CountryReleases(string Country, IReadOnlyList<FeatureReleaseView> Features);

    public static WebApplication MapFeatureReleases(this WebApplication app)
    {
        app.MapGet("/v1/features", async ([FromServices] IFeatureReleaseConsole releases, CancellationToken ct) =>
        {
            var all = new List<CountryReleases>();
            foreach (var country in releases.Countries) all.Add(new(country, await releases.ListAsync(country, ct) ?? []));
            return Results.Ok(all);
        }).RequireAuthorization(PlatformPolicies.FeatureRead);

        var act = app.Actions("/v1/features").RequireAuthorization(PlatformPolicies.FeatureChange);
        act.MapPost("/{country:regex(^[A-Z]{{2}}$)}/{key}", async (string country, string key, [FromBody] ReleaseBody body, [FromServices] IFeatureReleaseConsole releases,
            [FromServices] IConsoleActions actions, [FromServices] IPlatformCaller caller, CancellationToken ct) =>
        {
            var reason = PlatformReason.Valid(body.Reason)!; // the group's filter refused anything else
            if (await releases.SetAsync(country, key, new(body.Stage, body.Percentage, body.Organisations, body.ClinicalSignOff), Operator(caller), ct) is not { } id)
                return Results.NotFound();
            // shortcut: the release and this entry are in two databases (the country's and the console's), so they cannot share a transaction; add an outbox
            // or a reconcile job if an entry here is ever missing.
            await actions.RecordAsync($"feature.{body.Stage.ToString().ToLowerInvariant()}", Resource(key, country), id, null, reason, ct);
            return Results.NoContent();
        });

        // Turn off for all: every country the console reaches, at once. Refused for an always-on feature before anything changes.
        act.MapPost("/{key}/off", async (string key, [FromBody] PlatformEndpoints.ReasonBody body, [FromServices] IFeatureReleaseConsole releases,
            [FromServices] IConsoleActions actions, [FromServices] IPlatformCaller caller, CancellationToken ct) =>
        {
            var reason = PlatformReason.Valid(body.Reason)!;
            if (Features.Find(key) is not { } feature) return Results.NotFound();
            if (feature.AlwaysOn) return Results.ValidationProblem(new Dictionary<string, string[]> { ["stage"] = ["locked"] });
            foreach (var country in releases.Countries)
                if (await releases.SetAsync(country, key, new(ReleaseStage.Off, null, null, null), Operator(caller), ct) is { } id)
                    await actions.RecordAsync("feature.off", Resource(key, country), id, null, reason, ct);
            return Results.NoContent();
        });
        return app;
    }

    static string Resource(string key, string country) => $"feature:{key}:{country}";

    static Guid Operator(IPlatformCaller caller) => caller.MemberId ?? throw new InvalidOperationException("No signed-in console member.");
}
