using System.Data.Common;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Audit.Infrastructure;

/// <summary>
/// Writes audit events to the append-only ledger. Events without a client address or correlation id get the current request's
/// (<see cref="IRequestOrigin"/>), so rows written by use cases carry them as rows written by the endpoint filter do.
/// </summary>
public sealed class AuditLog(AuditDbContext db, IRequestOrigin? origin = null) : IAuditLog
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        db.Events.Add(await WithOriginAsync(auditEvent, cancellationToken));
        await db.SaveChangesAsync(cancellationToken); // throws on failure: the caller's request must not succeed unaudited
    }

    public async Task WriteAsync(AuditEvent auditEvent, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        ArgumentNullException.ThrowIfNull(transaction);
        // The caller's connection already carries the tenant setting, so row-level security applies to this insert too.
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(transaction.Connection!).Options;
        await using var shared = new AuditDbContext(options, new EventTenant(auditEvent.TenantId));
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        shared.Events.Add(await WithOriginAsync(auditEvent, cancellationToken));
        await shared.SaveChangesAsync(cancellationToken); // committed (or rolled back) by the caller, with its change
    }

    // The role and facility come from the session, never from the caller's event, so a use case cannot record a different authority.
    // Every row goes through here, so an unknown purpose of use is refused before anything is written (MDV-P2-024).
    async Task<AuditEvent> WithOriginAsync(AuditEvent e, CancellationToken cancellationToken)
    {
        AuditPurpose.Require(e.Purpose);
        return origin is null ? e : await WithSessionAsync(e, origin, cancellationToken);
    }

    static async Task<AuditEvent> WithSessionAsync(AuditEvent e, IRequestOrigin origin, CancellationToken cancellationToken) => new()
    {
        Id = e.Id, TenantId = e.TenantId, ActorUserId = e.ActorUserId, Action = e.Action, ResourceType = e.ResourceType, ResourceId = e.ResourceId,
        PatientId = e.PatientId, Purpose = e.Purpose, Outcome = e.Outcome, OccurredAt = e.OccurredAt, Detail = e.Detail,
        Role = e.Role ?? await origin.RoleAsync(cancellationToken), FacilityId = e.FacilityId ?? origin.FacilityId,
        ClientIp = e.ClientIp ?? origin.ClientIp, CorrelationId = e.CorrelationId ?? origin.CorrelationId,
    };

    sealed class EventTenant(Guid id) : ICurrentTenant { public Guid? TenantId => id; }
}
