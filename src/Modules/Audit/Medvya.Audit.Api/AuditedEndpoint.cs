using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
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
    /// </summary>
    public static TBuilder Audited<TBuilder>(this TBuilder builder, string action, string resourceType, string purpose = "treatment", string? patientRouteKey = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new AuditedEndpointMetadata(action, resourceType, purpose, patientRouteKey));
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var services = http.RequestServices;
            if (services.GetRequiredService<ICurrentTenant>().TenantId is not { } tenantId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");

            Guid? patientId = patientRouteKey is not null && Guid.TryParse(http.Request.RouteValues[patientRouteKey]?.ToString(), out var p) ? p : null;
            await services.GetRequiredService<IAuditLog>().WriteAsync(new AuditEvent
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ActorUserId = services.GetRequiredService<ICurrentUser>().UserId,
                Action = action,
                ResourceType = resourceType,
                PatientId = patientId,
                Purpose = purpose,
                Outcome = AuditOutcome.Allowed,
                OccurredAt = DateTimeOffset.UtcNow,
                ClientIp = http.Connection.RemoteIpAddress?.ToString(),
                CorrelationId = http.TraceIdentifier,
            }, http.RequestAborted);
            return await next(ctx);
        });
        return builder;
    }
}
