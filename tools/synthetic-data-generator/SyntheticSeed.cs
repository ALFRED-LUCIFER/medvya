using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.SyntheticData;

public sealed record CastMember(Guid UserId, string Name, Membership Membership);

public sealed record CastTenant(Tenant Tenant, Facility Facility, IReadOnlyList<CastMember> Members);

/// <summary>
/// The synthetic cast (docs/00-overview/personas.md) as tenants, facilities and one membership per role (MDV-P0-027).
/// Fixed ids make re-seeding idempotent; every name ends in "(synthetic)". Never runs outside Development/Staging/Test.
/// </summary>
public static class SyntheticSeed
{
    public const string Marker = " (synthetic)";

    static readonly string[] AllowedEnvironments = ["Development", "Staging", "Test"];

    // Recognisable fixed ids: 5e1d<tenant>-<kind>-…-<n>; kind 0 tenant, 1 facility, 2 user, 3 membership.
    static Guid Id(int tenant, int kind, int n) => Guid.Parse($"5e1d{tenant:x4}-{kind:x4}-4000-8000-{n:x12}");

    static CastTenant Clinic(int t, string name, string country, string region, string facility, string timeZone, params (string Name, Role Role)[] people)
    {
        var tenant = new Tenant { Id = Id(t, 0, 1), Name = name + Marker, CountryCode = country, ResidencyRegion = region };
        var site = new Facility { Id = Id(t, 1, 1), TenantId = tenant.Id, Name = facility + Marker, TimeZone = timeZone };
        var members = people.Select((p, i) => new CastMember(Id(t, 2, i + 1), p.Name + Marker, new Membership
        {
            Id = Id(t, 3, i + 1), TenantId = tenant.Id, UserId = Id(t, 2, i + 1), FacilityId = site.Id, Role = p.Role, Status = MembershipStatus.Active,
        })).ToList();
        return new(tenant, site, members);
    }

    // shortcut: user names live only here until the Identity user table exists (MDV-P0-011); seed users with these ids then.
    // shortcut: patient records (Meera Iyer, Deepak Nair, Fatima Al Mansoori) wait for the patient table; Patient rows here are portal user memberships only.
    public static readonly CastTenant India = Clinic(1, "Iyer Family Clinic", "IN", "centralindia", "Adyar", "Asia/Kolkata",
        ("Dr. Arjun Rao", Role.SoloDoctor),
        ("Dr. S. Menon", Role.HospitalDoctor),
        ("A. Joseph", Role.Receptionist),
        ("S. Thomas RN", Role.Nurse),
        ("Priya N.", Role.CareCoordinator),
        ("Meera Iyer", Role.Patient),
        ("K. Lakshmi", Role.PracticeAdmin), // not in the design cast; Dr. Rao already holds the doctor membership at Adyar
        ("R. Varghese", Role.HospitalIt));

    public static readonly CastTenant Uae = Clinic(2, "Al Noor Family Clinic", "AE", "uaenorth", "Abu Dhabi", "Asia/Dubai",
        ("Dr. Omar Haddad", Role.SoloDoctor),
        ("Fatima Al Mansoori", Role.Patient));

    public static IReadOnlyList<CastTenant> Tenants(bool includeUae) => includeUae ? [India, Uae] : [India];

    /// <summary>Throws unless the environment is explicitly Development, Staging or Test; a missing value is refused too.</summary>
    public static void EnsureNonProduction(string? environment)
    {
        if (!AllowedEnvironments.Contains(environment, StringComparer.Ordinal))
            throw new InvalidOperationException($"Synthetic seeding refused: environment '{environment}' is not Development, Staging or Test.");
    }

    /// <summary>Applies every module's migrations, then adds any missing cast rows. Existing rows are left as they are.</summary>
    public static async Task SeedAsync(string connectionString, string? environment, bool includeUae, CancellationToken ct = default)
    {
        EnsureNonProduction(environment);

        await using (var tenancy = Tenancy(connectionString, null)) await tenancy.Database.MigrateAsync(ct);
        await using (var audit = new AuditDbContext(Options<AuditDbContext>(connectionString, null), new FixedTenant(null)))
            await audit.Database.MigrateAsync(ct);

        foreach (var c in Tenants(includeUae))
        {
            // Writes run inside the tenant, so the RLS block predicates apply exactly as they do for the API.
            await using var db = Tenancy(connectionString, c.Tenant.Id);
            if (await db.Tenants.FindAsync([c.Tenant.Id], ct) is null) db.Tenants.Add(c.Tenant);
            if (await db.Facilities.FindAsync([c.Facility.Id], ct) is null) db.Facilities.Add(c.Facility);
            var existing = await db.Memberships.Select(m => m.Id).ToListAsync(ct);
            db.Memberships.AddRange(c.Members.Where(m => !existing.Contains(m.Membership.Id)).Select(m => m.Membership));
            await db.SaveChangesAsync(ct);
        }
    }

    sealed class FixedTenant(Guid? id) : ICurrentTenant { public Guid? TenantId => id; }

    static TenancyDbContext Tenancy(string connectionString, Guid? tenantId) =>
        new(Options<TenancyDbContext>(connectionString, tenantId), new FixedTenant(tenantId));

    static DbContextOptions<T> Options<T>(string connectionString, Guid? tenantId) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlServer(connectionString)
            .AddInterceptors(new TenantSessionInterceptor(new FixedTenant(tenantId))).Options;
}
