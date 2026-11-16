using System.Data.Common;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// Doctor fees as the prices of the two consultation services (MDV-P4-002). Writes use the caller's connection and transaction (as the patient-merge
/// participants do), so Tenancy's doctor row and its fee commit or roll back together. Tenancy audits the change that carries them.
/// </summary>
public sealed class DoctorFees(BillingDbContext db, ICurrentTenant tenant, ICurrentUser user, TimeProvider clock) : IDoctorFees
{
    /// <summary>The names a consultation service is created with; the clinic may rename it in Settings, Services.</summary>
    public static readonly IReadOnlyDictionary<ConsultationKind, string> DefaultNames = new Dictionary<ConsultationKind, string>
    {
        [ConsultationKind.New] = "Consultation (new visit)", [ConsultationKind.FollowUp] = "Consultation (follow-up)",
    };

    public async Task<IReadOnlyDictionary<Guid, ConsultationFees>> ForDoctorsAsync(IReadOnlyCollection<Guid> doctorIds, CancellationToken cancellationToken = default)
    {
        var (byKind, now) = (await VersionsAsync(db, cancellationToken), clock.GetUtcNow());
        return doctorIds.Distinct().ToDictionary(d => d, d => Fees(byKind, d, now));
    }

    public async Task<ConsultationFees> DefaultsAsync(CancellationToken cancellationToken = default) =>
        Fees(await VersionsAsync(db, cancellationToken), null, clock.GetUtcNow());

    public async Task SetAsync(Guid? doctorId, string countryCode, long newVisitMinor, long followUpMinor, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        var currency = BillingCountryRules.For(countryCode).Currency.Code;
        var now = clock.GetUtcNow();
        foreach (var (kind, amount) in new[] { (ConsultationKind.New, newVisitMinor), (ConsultationKind.FollowUp, followUpMinor) })
            shared.PriceVersions.Add(Version(await ServiceIdAsync(shared, kind, now, cancellationToken), doctorId, amount, currency, now));
        await shared.SaveChangesAsync(cancellationToken);
    }

    public async Task CopyAsync(Guid fromDoctorId, Guid toDoctorId, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        var now = clock.GetUtcNow();
        var versions = await shared.PriceVersions.AsNoTracking()
            .Where(v => (v.DoctorId == fromDoctorId || v.DoctorId == toDoctorId) && shared.Services.Any(s => s.Id == v.ServiceId && s.ConsultationKind != null && s.ArchivedAt == null))
            .ToListAsync(cancellationToken);
        if (versions.Any(v => v.DoctorId == toDoctorId)) return; // the doctor already has a price here (a location given back): kept
        foreach (var service in versions.GroupBy(v => v.ServiceId))
            if (PriceVersion.Resolve(service.Where(v => v.DoctorId == fromDoctorId), fromDoctorId, now) is { } from)
                shared.PriceVersions.Add(Version(service.Key, toDoctorId, from.AmountMinor, from.Currency, now));
        await shared.SaveChangesAsync(cancellationToken);
    }

    PriceVersion Version(Guid serviceId, Guid? doctorId, long amount, string currency, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), ServiceId = serviceId, DoctorId = doctorId,
        AmountMinor = amount, Currency = currency, EffectiveFrom = now, CreatedBy = user.UserId, CreatedAt = now,
    };

    /// <summary>
    /// The active consultation service of the kind, created when the clinic has none yet. Insert-if-absent on the one-per-kind index: a second doctor
    /// added at the same moment waits for the first transaction, then finds its service instead of failing on the index.
    /// </summary>
    async Task<Guid> ServiceIdAsync(BillingDbContext shared, ConsultationKind kind, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (id, tenantId, kindName) = (Guid.CreateVersion7(), tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), kind.ToString());
        var (name, category, tax) = (DefaultNames[kind], nameof(ServiceCategory.Consultation), nameof(TaxTreatment.Exempt));
        await shared.Database.ExecuteSqlAsync($"""
            INSERT INTO billing."Services" ("Id", "TenantId", "Name", "Category", "TaxTreatment", "ConsultationKind", "CreatedBy", "CreatedAt", "ChangedBy", "ChangedAt")
            VALUES ({id}, {tenantId}, {name}, {category}, {tax}, {kindName}, {user.UserId}, {now}, {user.UserId}, {now})
            ON CONFLICT ("TenantId", "ConsultationKind") WHERE "ConsultationKind" IS NOT NULL AND "ArchivedAt" IS NULL DO NOTHING
            """, cancellationToken);
        return await shared.Services.Where(s => s.ConsultationKind == kind && s.ArchivedAt == null).Select(s => s.Id).SingleAsync(cancellationToken);
    }

    static ConsultationFees Fees(ILookup<ConsultationKind, PriceVersion> byKind, Guid? doctorId, DateTimeOffset now) =>
        new(PriceVersion.Resolve(byKind[ConsultationKind.New], doctorId, now)?.AmountMinor, PriceVersion.Resolve(byKind[ConsultationKind.FollowUp], doctorId, now)?.AmountMinor);

    /// <summary>Every price of the active consultation services, by kind.</summary>
    // shortcut: reads every doctor's versions of the two services (a few rows per doctor); filter by doctor if a clinic reaches thousands of price changes.
    static async Task<ILookup<ConsultationKind, PriceVersion>> VersionsAsync(BillingDbContext db, CancellationToken cancellationToken) =>
        (await (from v in db.PriceVersions.AsNoTracking()
                join s in db.Services.AsNoTracking() on v.ServiceId equals s.Id
                where s.ConsultationKind != null && s.ArchivedAt == null
                select new { Kind = s.ConsultationKind!.Value, v }).ToListAsync(cancellationToken)).ToLookup(x => x.Kind, x => x.v);

    async Task<BillingDbContext> SharedAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var shared = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        return shared;
    }
}
