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
    public static TBuilder Audited<TBuilder>(this TBuilder builder, string action, string resourceType, string purpose = "treatment", string? patientRouteKey = null)
        where TBuilder : IEndpointConventionBuilder
    {
        var metadata = new AuditedEndpointMetadata(action, resourceType, purpose, patientRouteKey);
        builder.WithMetadata(metadata);
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (http.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is not { } tenantId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");

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
            await WriteAsync(http, metadata, tenantId, OutcomeOf(result));
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
        var metadata = new AuditedEndpointMetadata(action, resourceType, purpose, patientRouteKey);
        builder.WithMetadata(metadata);
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (http.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is not { } tenantId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
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
        builder.WithMetadata(new AuditedRefusalMetadata(action, resourceType, purpose));

    public static IServiceCollection AddDeniedAccessAudit(this IServiceCollection services) =>
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditDeniedAccess>();

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
            PatientId = patientId,
            Purpose = metadata.Purpose,
            Outcome = outcome,
            OccurredAt = DateTimeOffset.UtcNow,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            CorrelationId = http.TraceIdentifier,
        }, http.RequestAborted);
    }
}

/// <summary>
/// Records a forbidden request to an <c>.Audited()</c> (or <c>.AuditRefusals()</c>) endpoint, then answers as ASP.NET Core would (403). Only signed-in callers
/// inside a tenant leave an event: an audit row belongs to a tenant, and anonymous calls are refused before any tenant is known.
/// </summary>
public sealed class AuditDeniedAccess : IAuthorizationMiddlewareResultHandler
{
    readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden
            && Of(context.GetEndpoint()) is { } metadata
            && context.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is { } tenantId)
            await AuditedEndpoint.WriteAsync(context, metadata, tenantId, AuditOutcome.Denied);
        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    static AuditedEndpointMetadata? Of(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<AuditedEndpointMetadata>()
        ?? (endpoint?.Metadata.GetMetadata<AuditedRefusalMetadata>() is { } r ? new(r.Action, r.ResourceType, r.Purpose, null) : null);
}
