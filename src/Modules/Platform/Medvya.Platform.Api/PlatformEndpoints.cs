using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Platform.Api;

/// <summary>
/// Platform console authorisation (MDV-P5-040). The host's session check adds <see cref="KeyClaim"/> once the security key is verified in
/// this session, and <see cref="RoleClaim"/> from the live member row on every request, so a removal or role change applies at once.
/// </summary>
public static class PlatformPolicies
{
    public const string RoleClaim = "platform_role", KeyClaim = "platform_key", KeyVerified = "verified";
    /// <summary>Company sign-on done, key not yet: only the security-key steps and /v1/me accept this.</summary>
    public const string SignedOn = "platform.signed-on";
    /// <summary>Any member whose key is verified. Also the default and fallback policy, so a forgotten policy still needs the key.</summary>
    public const string Console = "platform.console";
    public const string Owner = "platform.owner";
    public const string AuditRead = "platform.audit.read";

    public static AuthorizationBuilder AddPlatformPolicies(this AuthorizationBuilder builder)
    {
        var console = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(KeyClaim, KeyVerified).Build();
        return builder
            .SetDefaultPolicy(console)
            .SetFallbackPolicy(console)
            .AddPolicy(SignedOn, p => p.RequireAuthenticatedUser())
            .AddPolicy(Console, console)
            .AddPolicy(Owner, p => p.Combine(console).RequireClaim(RoleClaim, nameof(PlatformRole.Owner)))
            .AddPolicy(AuditRead, p => p.Combine(console).RequireClaim(RoleClaim, nameof(PlatformRole.Owner), nameof(PlatformRole.Operations)));
    }
}

/// <summary>Marks an endpoint that refuses a request without a reason (<see cref="PlatformEndpoints.Actions"/>); a test checks every write has it.</summary>
public sealed class RequiresReasonMetadata;

public static class PlatformEndpoints
{
    public sealed record ReasonBody(string? Reason) : IReasoned;

    /// <summary>
    /// A group for console actions: the body must carry a reason (<see cref="PlatformReason"/>) or the request is refused with 400 before
    /// anything happens. The reason is then saved with the action's audit entry.
    /// </summary>
    public static RouteGroupBuilder Actions(this IEndpointRouteBuilder app, string prefix) =>
        app.MapGroup(prefix).WithMetadata(new RequiresReasonMetadata()).AddEndpointFilter(async (ctx, next) =>
            PlatformReason.Valid(ctx.Arguments.OfType<IReasoned>().SingleOrDefault()?.Reason) is null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["required"] })
                : await next(ctx)).AddEndpointFilter(MapErrors);

    public static WebApplication MapPlatformConsole(this WebApplication app)
    {
        app.MapGet("/v1/team", ([FromServices] IPlatformTeam team, CancellationToken ct) => team.ListAsync(ct)).RequireAuthorization(PlatformPolicies.Owner);
        app.MapGet("/v1/audit", ([FromServices] IPlatformAuditLog log, DateTimeOffset? before, Guid? beforeId, int? limit, CancellationToken ct) =>
            log.ListAsync(before, beforeId, limit ?? 50, ct)).RequireAuthorization(PlatformPolicies.AuditRead);

        var team = app.Actions("/v1/team").RequireAuthorization(PlatformPolicies.Owner);
        team.MapPost("/invitations", async ([FromBody] Invite body, [FromServices] IPlatformTeam t, CancellationToken ct) =>
            Results.Created((string?)null, await t.InviteAsync(body with { Reason = PlatformReason.Valid(body.Reason) }, ct)));
        team.MapPost("/{id:guid}/recovery", async (Guid id, [FromBody] ReasonBody body, [FromServices] IPlatformTeam t, CancellationToken ct) =>
            await t.IssueRecoveryAsync(id, PlatformReason.Valid(body.Reason)!, ct) switch
            {
                (TeamOutcome.Done, var link) => Results.Ok(link),
                (TeamOutcome.NotFound, _) => Results.NotFound(),
                _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another owner must recover your key"),
            });
        team.MapPost("/{id:guid}/removal", async (Guid id, [FromBody] ReasonBody body, [FromServices] IPlatformTeam t, CancellationToken ct) =>
            await t.RemoveAsync(id, PlatformReason.Valid(body.Reason)!, ct) switch
            {
                TeamOutcome.Done => Results.NoContent(),
                TeamOutcome.NotFound => Results.NotFound(),
                _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not yourself, and not the last owner"),
            });
        team.MapPost("/access-review", async ([FromBody] ReasonBody body, [FromServices] IPlatformTeam t, CancellationToken ct) =>
        {
            await t.RecordAccessReviewAsync(PlatformReason.Valid(body.Reason)!, ct);
            return Results.NoContent();
        });

        app.Actions("/v1/audit").RequireAuthorization(PlatformPolicies.Owner)
            .MapPost("/export", async ([FromBody] ReasonBody body, [FromServices] IPlatformAuditLog log, CancellationToken ct) =>
                Results.File(System.Text.Encoding.UTF8.GetBytes(await log.ExportCsvAsync(PlatformReason.Valid(body.Reason)!, ct)), "text/csv; charset=utf-8",
                    "platform-audit-log.csv"));
        return app;
    }

    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try { return await next(ctx); }
        catch (PlatformValidationException e) { return Results.ValidationProblem(e.Errors.ToDictionary(x => x.Key, x => x.Value)); }
    }
}
