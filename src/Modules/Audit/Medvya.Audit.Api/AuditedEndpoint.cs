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
    /// Writes exactly one audit event per request, <b>before</b> the handler runs: if the audit write fails, the request
    /// fails and no patient data is returned (fail closed). Requests without a tenant are refused.
    /// A request the authorization policy refuses never reaches this filter; <see cref="AuditDeniedAccess"/> records it instead.
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

            await WriteAsync(http, metadata, tenantId, AuditOutcome.Allowed);
            return await next(ctx);
        });
        return builder;
    }

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
