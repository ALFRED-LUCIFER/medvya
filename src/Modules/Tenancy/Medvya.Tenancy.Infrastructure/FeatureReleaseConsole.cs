using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Medvya's release switches for the platform console (MDV-P5-041), one database per country (ADR-003: the row lives where the clinics are). Each connection
/// is the narrow login medvya_features: it can read and write tenancy."FeatureReleases" and nothing else (table-rights.sql).
/// </summary>
public sealed class FeatureReleaseConsole(IReadOnlyDictionary<string, string> countries, TimeProvider clock) : IFeatureReleaseConsole
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public IReadOnlyList<string> Countries { get; } = [.. countries.Keys.Order(StringComparer.Ordinal)];

    TenancyDbContext? Open(string country) => countries.TryGetValue(country, out var sql)
        ? new(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(PostgresDataSources.For(sql)).Options, new NoTenant())
        : null;

    public async Task<IReadOnlyList<FeatureReleaseView>?> ListAsync(string country, CancellationToken cancellationToken = default)
    {
        await using var db = Open(country);
        if (db is null) return null;
        var rows = await db.FeatureReleases.AsNoTracking().Where(r => r.Country == country).ToDictionaryAsync(r => r.Key, cancellationToken);
        return [.. Features.All.Where(f => f.Countries.Contains(country)).Select(f => rows.TryGetValue(f.Key, out var r)
            ? new FeatureReleaseView(f.Key, f.Clinical, f.ClinicSwitch, f.AlwaysOn, f.AlwaysOn ? ReleaseStage.Everyone : r.Stage, false, r.Percentage, r.Organisations, r.ClinicalSignOff, r.ChangedAt)
            : new FeatureReleaseView(f.Key, f.Clinical, f.ClinicSwitch, f.AlwaysOn, f.Initial, !f.AlwaysOn, 0, [], null, null))];
    }

    public async Task<Guid?> SetAsync(string country, string key, ReleaseChange change, Guid operatorId, CancellationToken cancellationToken = default)
    {
        if (Features.Find(key) is not { } feature || !feature.Countries.Contains(country)) return null;
        await using var db = Open(country);
        if (db is null) return null;
        var row = await db.FeatureReleases.SingleOrDefaultAsync(r => r.Country == country && r.Key == key, cancellationToken);
        var signOff = string.IsNullOrWhiteSpace(change.ClinicalSignOff) ? null : change.ClinicalSignOff.Trim();
        var organisations = change.Organisations?.Distinct().ToArray() ?? row?.Organisations ?? [];

        var errors = new List<ValidationError>();
        if (feature.AlwaysOn) errors.Add(new("stage", "locked"));
        if (!Enum.IsDefined(change.Stage)) errors.Add(new("stage", "invalid"));
        if (signOff is { Length: > FeatureRelease.SignOffMax }) errors.Add(new("clinicalSignOff", "too_long"));
        // Clinical safety: a clinical feature reaches every organisation only once a clinical sign-off is on record for this country.
        if (feature.Clinical && change.Stage == ReleaseStage.Everyone && signOff is null && row?.ClinicalSignOff is null) errors.Add(new("clinicalSignOff", "required"));
        if (change.Stage == ReleaseStage.Percentage && change.Percentage is not (>= 1 and <= 99)) errors.Add(new("percentage", "range"));
        if (change.Stage == ReleaseStage.Beta && organisations.Length == 0) errors.Add(new("organisations", "required"));
        if (organisations.Length > FeatureRelease.OrganisationsMax) errors.Add(new("organisations", "too_many"));
        SettingsValidationException.ThrowIfAny(errors);

        var now = clock.GetUtcNow();
        if (row is null)
        {
            row = new FeatureRelease { Country = country, Key = key };
            db.FeatureReleases.Add(row);
        }
        row.Stage = change.Stage;
        if (change.Stage == ReleaseStage.Percentage) row.Percentage = change.Percentage!.Value;
        row.Organisations = organisations;
        if (signOff is not null && row.ClinicalSignOff is null)
        {
            row.ClinicalSignOff = signOff;
            row.SignOffByMemberId = operatorId;
            row.SignOffAt = now;
        }
        row.ChangedByMemberId = operatorId;
        row.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }
}
