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

public static class AuditedEndpoint
{
    /// <summary>
    /// Writes exactly one audit event per request, once the endpoint has answered and <b>before</b> the answer is sent: if the
    /// audit write fails, the request fails and no patient data is returned (fail closed). Requests without a tenant are refused.
    /// The outcome follows one rule for every endpoint (<see cref="OutcomeOf"/>): 401/403 is Denied, 404 is NotFound, anything
    /// else Allowed. Endpoints therefore map their own errors to results inside this filter, never around it.
    /// A request the authorization policy refuses never reaches this filter; <see cref="AuditDeniedAccess"/> records it instead.
    /// shortcut: for a write, the row follows the use case's commit, so an audit failure after the commit answers 500 but leaves
    /// the change without its row; write audit rows in the use case's transaction (outbox) when a module needs that (MDV-P0-017).
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

    /// <summary>The audit outcome of an endpoint's answer: 401/403 Denied, 404 NotFound (e.g. a record outside the caller's facility), else Allowed.</summary>
    public static AuditOutcome OutcomeOf(object? result) => StatusOf(result) switch
    {
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => AuditOutcome.Denied,
        StatusCodes.Status404NotFound => AuditOutcome.NotFound,
        _ => AuditOutcome.Allowed,
    };

    // A plain value is serialised as 200; typed unions (Results<…>) wrap the result that runs.
    static int StatusOf(object? result) => result switch
    {
        INestedHttpResult nested => StatusOf(nested.Result),
        IStatusCodeHttpResult { StatusCode: { } status } => status,
        _ => StatusCodes.Status200OK,
    };

    /// <summary>
    /// A refused call to an audited endpoint is itself audited (outcome Denied, PRD v2 P0-06), so every request to patient data
    /// leaves exactly one event whatever the decision. Register with the Audit module; it needs <see cref="IAuditLog"/>.
    /// </summary>
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
/// Records a forbidden request to an <c>.Audited()</c> endpoint, then answers as ASP.NET Core would (403). Only signed-in callers
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
            && context.GetEndpoint()?.Metadata.GetMetadata<AuditedEndpointMetadata>() is { } metadata
            && context.RequestServices.GetRequiredService<ICurrentTenant>().TenantId is { } tenantId)
            await AuditedEndpoint.WriteAsync(context, metadata, tenantId, AuditOutcome.Denied);
        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
