using System.Collections.Concurrent;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Medvya's release rows of this country's database, shared by every request for at most <see cref="MaxAge"/>, and each tenant's country (fixed for life,
/// ADR-003). <see cref="InternalOrganisations"/>: Medvya's own test organisations in this deployment (config Features:InternalOrganisations).
/// </summary>
public sealed class FeatureReleaseCache(TimeProvider clock, IReadOnlySet<Guid> internalOrganisations)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    sealed record Snapshot(DateTimeOffset LoadedAt, IReadOnlyList<FeatureRelease> Rows);
    volatile Snapshot? snapshot;
    readonly ConcurrentDictionary<Guid, string> countries = new();

    public IReadOnlySet<Guid> InternalOrganisations => internalOrganisations;

    public async Task<IReadOnlyList<FeatureRelease>> ReleasesAsync(TenancyDbContext db, CancellationToken cancellationToken)
    {
        if (snapshot is { } s && clock.GetUtcNow() - s.LoadedAt < MaxAge) return s.Rows;
        var rows = await db.FeatureReleases.AsNoTracking().ToListAsync(cancellationToken);
        snapshot = new(clock.GetUtcNow(), rows);
        return rows;
    }

    public async Task<string?> CountryAsync(TenancyDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        if (countries.TryGetValue(tenantId, out var known)) return known;
        var country = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.CountryCode).SingleOrDefaultAsync(cancellationToken);
        if (country is not null) countries[tenantId] = country;
        return country;
    }
}

/// <summary>
/// A feature is on when Medvya's release reaches the organisation (cached, at most 60 s old) and, where it has one, the practice's own switch is on. The
/// practice's switch is read from the database on every check, so the owner turning it off takes effect on the next request.
/// </summary>
public sealed class FeatureFlags(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, FeatureReleaseCache releases) : IFeatureFlags
{
    public async Task<bool> IsEnabledAsync(Feature feature, CancellationToken cancellationToken = default) =>
        await IsReleasedAsync(feature, cancellationToken)
        && (!feature.ClinicSwitch || await db.FeatureFlags.AnyAsync(f => f.Key == feature.Key && f.Enabled, cancellationToken));

    public async Task<bool> IsReleasedAsync(Feature feature, CancellationToken cancellationToken = default) =>
        tenant.TenantId is { } id && await releases.CountryAsync(db, id, cancellationToken) is { } country
        && FeatureRelease.Reaches(feature, (await releases.ReleasesAsync(db, cancellationToken)).FirstOrDefault(r => r.Country == country && r.Key == feature.Key),
            id, country, releases.InternalOrganisations);

    public async Task<IReadOnlyList<string>> EnabledKeysAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is null) return [];
        var on = await db.FeatureFlags.Where(f => f.Enabled).Select(f => f.Key).ToListAsync(cancellationToken);
        var keys = new List<string>();
        foreach (var feature in Features.All.Where(f => !f.AlwaysOn)) // always on needs no key: nothing hides it
            if ((!feature.ClinicSwitch || on.Contains(feature.Key)) && await IsReleasedAsync(feature, cancellationToken)) keys.Add(feature.Key);
        return [.. keys.Order(StringComparer.Ordinal)];
    }

    public async Task SetAsync(Feature feature, bool enabled, string owner, string description, CancellationToken cancellationToken = default)
    {
        if (!feature.ClinicSwitch) throw new ArgumentException("This feature has no clinic switch; Medvya's release alone decides.", nameof(feature));
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 100) throw new ArgumentException("Every flag needs an owner (max 100).", nameof(owner));
        if (string.IsNullOrWhiteSpace(description) || description.Length > 500) throw new ArgumentException("Every flag needs a description (max 500).", nameof(description));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var flag = await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == feature.Key, cancellationToken);
        if (flag is null)
        {
            flag = new FeatureFlag { Id = Guid.CreateVersion7(), TenantId = tenantId, Key = feature.Key, Owner = owner, Description = description, CreatedBy = user.UserId, CreatedAt = now };
            db.FeatureFlags.Add(flag);
        }
        flag.Enabled = enabled;
        flag.Owner = owner;
        flag.Description = description;
        flag.ChangedBy = user.UserId;
        flag.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);

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
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
