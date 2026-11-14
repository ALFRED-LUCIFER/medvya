using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Audit.Api;

/// <summary>Endpoint metadata: this endpoint reads or writes patient data and is audited (PRD v2 P0-06).</summary>
public sealed record AuditedEndpointMetadata(string Action, string ResourceType, string Purpose, string? PatientRouteKey);

/// <summary>
/// Endpoint metadata: the endpoint carries no patient data itself but opens a way to it (a real-time connection), so a request
/// the authorization policy refuses is audited Denied as on an audited endpoint; what an allowed caller then does is audited there.
/// </summary>
public sealed record AuditedRefusalMetadata(string Action, string ResourceType, string Purpose);

public static class AuditedEndpoint
{
    /// <summary>
    /// For <b>reads</b>: writes exactly one audit event per request, once the endpoint has answered and <b>before</b> the answer is
    /// sent: if the audit write fails, the request fails and no patient data is returned (fail closed). Requests without a tenant
    /// are refused. The outcome follows one rule for every endpoint (<see cref="OutcomeFor"/>). Endpoints therefore map their own
    /// errors to results inside this filter, never around it. Writes use <see cref="AuditedByUseCase"/> instead, so the row commits
    /// with the change. A request the authorization policy refuses never reaches this filter; <see cref="AuditDeniedAccess"/>
    /// records it instead.
    /// </summary>
    public static TBuilder Audited<TBuilder>(this TBuilder builder, string action, string resourceType, string purpose = "treatment", string? patientRouteKey = null, TimeSpan? window = null)
        where TBuilder : IEndpointConventionBuilder
    {
        var metadata = new AuditedEndpointMetadata(action, resourceType, AuditPurpose.Require(purpose), patientRouteKey);
        builder.WithMetadata(metadata);
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (http.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is not { } tenantId)
                return await RefuseWithoutTenantAsync(http, metadata);

            object? result;
            try
            {
                result = await next(ctx);
            }
            catch
            {
                await WriteAsync(http, metadata, tenantId, AuditOutcome.Allowed); // not refused: it failed while running
                throw;
            }
            var outcome = OutcomeOf(result);
            // With a window, the same user's identical Allowed read is written once per window; every other outcome is always written.
            var windows = http.RequestServices.GetRequiredService<AuditWindow>();
            var key = window is not null && outcome == AuditOutcome.Allowed && http.RequestServices.GetRequiredService<ICurrentUser>().UserId is { } me
                ? AuditWindow.Key(tenantId, me, action, http.Request.Path + http.Request.QueryString.Value) : null;
            if (key is not null && windows.Covered(key)) return result;
            await WriteAsync(http, metadata, tenantId, outcome);
            if (key is not null) windows.Record(key, window!.Value); // only after the row is written: a failed write must not cover later reads
            return result;
        });
        return builder;
    }

    /// <summary>
    /// The audit outcome of an HTTP answer, one rule for reads and writes: 401/403 Denied, 404 NotFound (e.g. a record outside the
    /// caller's facility), 400/409/422 Rejected (invalid input, an unresolved duplicate, a slot taken), anything else Allowed.
    /// </summary>
    public static AuditOutcome OutcomeFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => AuditOutcome.Denied,
        StatusCodes.Status404NotFound => AuditOutcome.NotFound,
        StatusCodes.Status400BadRequest or StatusCodes.Status409Conflict or StatusCodes.Status422UnprocessableEntity => AuditOutcome.Rejected,
        _ => AuditOutcome.Allowed,
    };

    /// <summary>The audit outcome of an endpoint's result (<see cref="OutcomeFor"/> of its status code).</summary>
    public static AuditOutcome OutcomeOf(object? result) => OutcomeFor(StatusOf(result));

    // A plain value is serialised as 200; typed unions (Results<…>) wrap the result that runs.
    static int StatusOf(object? result) => result switch
    {
        INestedHttpResult nested => StatusOf(nested.Result),
        IStatusCodeHttpResult { StatusCode: { } status } => status,
        _ => StatusCodes.Status200OK,
    };

    /// <summary>
    /// For <b>writes</b>: the use case writes the Allowed event inside the database transaction of its change
    /// (<see cref="IAuditLog.WriteAsync(AuditEvent, System.Data.Common.DbTransaction, CancellationToken)"/>), so a committed change
    /// always has its row and a failed audit write rolls the change back. When the endpoint answers anything but Allowed
    /// (<see cref="OutcomeFor"/>: refused, not found, rejected), nothing was changed and this filter writes that one row, with
    /// codes only. A request that fails (5xx) has rolled back and leaves no row. Refusals by the authorization policy are audited
    /// by <see cref="AuditDeniedAccess"/>. The use case must audit its allowed path; the endpoint tests prove it per endpoint.
    /// </summary>
    public static TBuilder AuditedByUseCase<TBuilder>(this TBuilder builder, string action, string resourceType, string purpose, string? patientRouteKey = null)
        where TBuilder : IEndpointConventionBuilder
    {
        var metadata = new AuditedEndpointMetadata(action, resourceType, AuditPurpose.Require(purpose), patientRouteKey);
        builder.WithMetadata(metadata);
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (http.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is not { } tenantId)
                return await RefuseWithoutTenantAsync(http, metadata);
            var result = await next(ctx);
            if (OutcomeOf(result) is var outcome and not AuditOutcome.Allowed) await WriteAsync(http, metadata, tenantId, outcome);
            return result;
        });
        return builder;
    }

    /// <summary>
    /// A refused call to an audited endpoint is itself audited (outcome Denied, PRD v2 P0-06), so every request to patient data
    /// leaves exactly one event whatever the decision. Register with the Audit module; it needs <see cref="IAuditLog"/>.
    /// </summary>
    /// <summary>Audits refusals by the authorization policy only (<see cref="AuditedRefusalMetadata"/>), e.g. on a SignalR hub's routes.</summary>
    public static TBuilder AuditRefusals<TBuilder>(this TBuilder builder, string action, string resourceType, string purpose = "treatment")
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AuditedRefusalMetadata(action, resourceType, AuditPurpose.Require(purpose)));

    public static IServiceCollection AddDeniedAccessAudit(this IServiceCollection services) =>
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditDeniedAccess>();

    /// <summary>A caller with no facility is refused 403, and the refusal is recorded in the platform chain (a signed-in caller only: an anonymous 401 is not written, so a scan cannot fill the ledger).</summary>
    internal static async Task<IResult> RefuseWithoutTenantAsync(HttpContext http, AuditedEndpointMetadata metadata)
    {
        await WritePlatformAsync(http, metadata);
        return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
    }

    internal static Task WritePlatformAsync(HttpContext http, AuditedEndpointMetadata metadata) =>
        WritePlatformAsync(http, metadata.Action, metadata.ResourceType, metadata.Purpose);

    /// <summary>
    /// Records a Denied event, for a signed-in caller only, in the platform chain. The same user's identical refusal (same action and path) is
    /// written once a minute, so a client that keeps calling with no facility cannot flood the one chain; the first is always written, and only
    /// after it succeeded does the window start (fail closed).
    /// </summary>
    // shortcut: the platform chain is read only through the database (the review login); a console reader comes with the platform console (MDV-P1-064).
    public static async Task WritePlatformAsync(HttpContext http, string action, string resourceType, string purpose, Guid? resourceId = null, AuditOutcome outcome = AuditOutcome.Denied)
    {
        var services = http.RequestServices;
        if (services.GetRequiredService<ICurrentUser>().UserId is not { } user) return;
        var windows = services.GetRequiredService<AuditWindow>();
        var key = AuditWindow.Key(PlatformTenant.Id, user, action, http.Request.Path);
        if (windows.Covered(key)) return;
        await services.GetRequiredService<IPlatformAuditLog>().WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformTenant.Id, ActorUserId = user, Action = action, ResourceType = resourceType, ResourceId = resourceId,
            Purpose = purpose, Outcome = outcome, OccurredAt = DateTimeOffset.UtcNow,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(), CorrelationId = http.TraceIdentifier,
        }, http.RequestAborted);
        windows.Record(key, TimeSpan.FromMinutes(1));
    }

    internal static Task WriteAsync(HttpContext http, AuditedEndpointMetadata metadata, Guid tenantId, AuditOutcome outcome)
    {
        var services = http.RequestServices;
        Guid? patientId = metadata.PatientRouteKey is not null && Guid.TryParse(http.Request.RouteValues[metadata.PatientRouteKey]?.ToString(), out var p) ? p : null;
        return services.GetRequiredService<IAuditLog>().WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorUserId = services.GetRequiredService<ICurrentUser>().UserId,
            Action = metadata.Action,
            ResourceType = metadata.ResourceType,
            ResourceId = http.Items["audit.resource"] as Guid?, // an endpoint's error mapping may name the record it refused (ids only)
            PatientId = patientId,
            Purpose = metadata.Purpose,
            Outcome = outcome,
            OccurredAt = DateTimeOffset.UtcNow,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = http.TraceIdentifier,
            Detail = http.Items["audit.detail"] as string,
        }, http.RequestAborted);
    }
}

/// <summary>
/// Records a forbidden request to an <c>.Audited()</c> (or <c>.AuditRefusals()</c>) endpoint, then answers as ASP.NET Core would (403). Signed-in callers leave an
/// event, in their tenant's chain or, with no facility chosen, in the platform chain. Anonymous 401s are not written (a scan must not fill the ledger).
/// </summary>
public sealed class AuditDeniedAccess : IAuthorizationMiddlewareResultHandler
{
    readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden && Of(context.GetEndpoint()) is { } metadata)
        {
            if (context.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is { } tenantId)
                await AuditedEndpoint.WriteAsync(context, metadata, tenantId, AuditOutcome.Denied);
            else
                await AuditedEndpoint.WritePlatformAsync(context, metadata); // signed in but no facility chosen yet
        }
        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    static AuditedEndpointMetadata? Of(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<AuditedEndpointMetadata>()
        ?? (endpoint?.Metadata.GetMetadata<AuditedRefusalMetadata>() is { } r ? new(r.Action, r.ResourceType, r.Purpose, null) : null);
}
