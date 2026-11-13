using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Audit.Infrastructure;

/// <summary>Writes to the platform chain on its own connection scoped to <see cref="PlatformTenant.Id"/>, so the request's own (absent) tenant never applies.</summary>
public sealed class PlatformAuditLog : IPlatformAuditLog
{
    static readonly Platform Scope = new();
    readonly DbContextOptions<AuditDbContext> options;

    public PlatformAuditLog(string connectionString) =>
        options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(new TenantSessionInterceptor(Scope)).Options;

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var db = new AuditDbContext(options, Scope);
        await new AuditLog(db).WriteAsync(new AuditEvent
        {
            Id = auditEvent.Id, TenantId = PlatformTenant.Id, ActorUserId = auditEvent.ActorUserId, Action = auditEvent.Action, ResourceType = auditEvent.ResourceType, ResourceId = auditEvent.ResourceId,
            Purpose = auditEvent.Purpose, Outcome = auditEvent.Outcome, OccurredAt = auditEvent.OccurredAt, Detail = auditEvent.Detail, ClientIp = auditEvent.ClientIp, CorrelationId = auditEvent.CorrelationId,
        }, cancellationToken);
    }

    sealed class Platform : ICurrentTenant { public Guid? TenantId => PlatformTenant.Id; }
}
