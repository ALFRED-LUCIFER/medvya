using System.Text.RegularExpressions;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Table-backed flags, read from the database on every check so turning one off takes effect on the next request.
/// shortcut: one indexed lookup per gated request, no cache; add a short (&lt;= 60 s) cache if flag reads show up in latency.
/// </summary>
public sealed partial class FeatureFlags(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit) : IFeatureFlags
{
    [GeneratedRegex("^[a-z][a-z0-9]*([._-][a-z0-9]+)*$")]
    private static partial Regex KeyFormat();

    public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) =>
        tenant.TenantId is null
            ? Task.FromResult(false)
            : db.FeatureFlags.AnyAsync(f => f.Key == key && f.Enabled, cancellationToken);

    public async Task<IReadOnlyList<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) =>
        tenant.TenantId is null ? [] : await db.FeatureFlags.Where(f => f.Enabled).OrderBy(f => f.Key).Select(f => f.Key).ToListAsync(cancellationToken);

    public async Task SetAsync(string key, bool enabled, string owner, string description, CancellationToken cancellationToken = default)
    {
        if (key.Length > 100 || !KeyFormat().IsMatch(key)) throw new ArgumentException("Flag key must be lowercase letters, digits and . _ - (max 100).", nameof(key));
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 100) throw new ArgumentException("Every flag needs an owner (max 100).", nameof(owner));
        if (string.IsNullOrWhiteSpace(description) || description.Length > 500) throw new ArgumentException("Every flag needs a description (max 500).", nameof(description));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

        var now = DateTimeOffset.UtcNow;
        var flag = await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == key, cancellationToken);
        if (flag is null)
        {
            flag = new FeatureFlag { Id = Guid.CreateVersion7(), TenantId = tenantId, Key = key, Owner = owner, Description = description, CreatedBy = user.UserId, CreatedAt = now };
            db.FeatureFlags.Add(flag);
        }
        flag.Enabled = enabled;
        flag.Owner = owner;
        flag.Description = description;
        flag.ChangedBy = user.UserId;
        flag.ChangedAt = now;

        // Audit first, as the .Audited() filter does: a failed audit write stops the change (fail closed).
        // shortcut: two DbContexts with no shared transaction, so a failed save can leave an audit row for a change that did not land; share the transaction when an outbox exists.
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorUserId = user.UserId,
            Action = enabled ? "flag.enable" : "flag.disable",
            ResourceType = "feature_flag",
            ResourceId = flag.Id,
            Purpose = "support",
            Outcome = AuditOutcome.Allowed,
            OccurredAt = now,
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
