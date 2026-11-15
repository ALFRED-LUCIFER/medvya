using System.Text.RegularExpressions;
using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.Platform.Infrastructure;

/// <summary>Plans (MDV-P5-042): an edit adds the next version with its audit entry in one save; no version is ever changed.</summary>
sealed partial class PlatformPlans(PlatformDbContext db, IPlatformCaller caller, TimeProvider clock) : IPlatformPlans
{
    // shortcut: any well-formed key is accepted; check keys against the code feature list once MDV-P5-041 adds it.
    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex FeatureKey();

    public async Task<IReadOnlyList<PlanVersionView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var versions = await db.PlanVersions.AsNoTracking().Include(v => v.Prices)
            .OrderBy(v => v.Code).ThenByDescending(v => v.Version).ToListAsync(cancellationToken);
        var ids = versions.Select(v => v.CreatedByMemberId).OfType<Guid>().Distinct().ToList();
        var names = await db.Members.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);
        return [.. versions.Select(v => View(v, v.CreatedByMemberId is { } id ? names.GetValueOrDefault(id) : null))];
    }

    public async Task<(PlanOutcome Outcome, PlanVersionView? Saved)> SaveAsync(string code, PlanEdit edit, string reason, CancellationToken cancellationToken = default)
    {
        var latest = await db.PlanVersions.AsNoTracking().Where(v => v.Code == code).OrderByDescending(v => v.Version).FirstOrDefaultAsync(cancellationToken);
        if (latest is null) return (PlanOutcome.NotFound, null);
        var features = Validate(edit);
        if (edit.BaseVersion != latest.Version) return (PlanOutcome.Stale, null);

        var now = clock.GetUtcNow();
        var next = new PlanVersion
        {
            Code = code, Version = latest.Version + 1, PerDoctor = latest.PerDoctor, Custom = latest.Custom,
            Doctors = edit.Doctors, Staff = edit.Staff, Locations = edit.Locations, AssistVisitsPerDoctorMonth = edit.AssistVisitsPerDoctorMonth,
            TrialDays = edit.TrialDays!.Value, Features = features, CreatedAt = now, CreatedByMemberId = caller.MemberId, Reason = reason,
        };
        next.Prices.AddRange(edit.Prices!.Select(p => new PlanPrice
        {
            PlanVersionId = next.Id, Country = p.Country!, Currency = PlanPrice.Currencies[p.Country!], Monthly = p.Monthly, Yearly = p.Yearly, AssistMonthly = p.AssistMonthly,
        }));
        db.PlanVersions.Add(next);
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "plan.version.create", "plan", next.Id, reason));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return (PlanOutcome.Stale, null); // another edit took this version number between our read and our save
        }
        var name = caller.MemberId is { } me ? await db.Members.Where(m => m.Id == me).Select(m => m.DisplayName).SingleOrDefaultAsync(cancellationToken) : null;
        return (PlanOutcome.Saved, View(next, name));
    }

    /// <summary>Refuses an edit that is not a whole, sensible version; returns its feature keys trimmed, without duplicates and sorted.</summary>
    static string[] Validate(PlanEdit edit)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void AtLeast(string field, int? value, int min) { if (value < min) errors[field] = ["range"]; }
        AtLeast("doctors", edit.Doctors, 1);
        AtLeast("staff", edit.Staff, 0);
        AtLeast("locations", edit.Locations, 1);
        AtLeast("assistVisitsPerDoctorMonth", edit.AssistVisitsPerDoctorMonth, 0);
        // Required: a missing value must not silently become "no trial".
        if (edit.TrialDays is null) errors["trialDays"] = ["required"];
        else if (edit.TrialDays is < 0 or > PlanVersion.TrialDaysMax) errors["trialDays"] = ["range"];

        var features = (edit.Features ?? []).Select(f => f?.Trim() ?? "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (features.Length > PlanVersion.FeaturesMax || features.Any(f => f.Length > PlanVersion.FeatureKeyMax || !FeatureKey().IsMatch(f)))
            errors["features"] = ["invalid"];

        // Every country deployment once, each amount zero or more (minor units). The currency is the country's, never the client's.
        var prices = edit.Prices ?? [];
        if (prices.Count != PlanPrice.Currencies.Count || prices.Select(p => p.Country).Distinct().Count() != prices.Count
            || prices.Any(p => p.Country is null || !PlanPrice.Currencies.ContainsKey(p.Country)))
            errors["prices"] = ["countries"];
        else if (prices.Any(p => p.Monthly < 0 || p.Yearly < 0 || p.AssistMonthly < 0))
            errors["prices"] = ["range"];
        PlatformValidationException.ThrowIfAny(errors);
        return features;
    }

    static PlanVersionView View(PlanVersion v, string? createdBy) => new(v.Id, v.Code, v.Version, v.PerDoctor, v.Custom, v.Doctors, v.Staff, v.Locations,
        v.AssistVisitsPerDoctorMonth, v.TrialDays, v.Features,
        [.. v.Prices.OrderByDescending(p => p.Country, StringComparer.Ordinal).Select(p => new PlanPriceView(p.Country, p.Currency, p.Monthly, p.Yearly, p.AssistMonthly))],
        v.CreatedAt, createdBy, v.Reason);
}
