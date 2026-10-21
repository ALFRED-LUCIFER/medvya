using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Medvya.SyntheticData;

public sealed record CastMember(Guid UserId, string Name, Membership Membership);

public sealed record CastPatient(Patient Patient, IReadOnlyList<PatientIdentifier> Identifiers, PayerCoverage Payer);

public sealed record CastTenant(Tenant Tenant, Facility Facility, IReadOnlyList<CastMember> Members)
{
    public IReadOnlyList<CastPatient> Patients { get; init; } = [];
    public IReadOnlyList<ScheduleResource> Resources { get; init; } = [];
    public IReadOnlyList<ScheduleTemplate> Templates { get; init; } = [];
}

/// <summary>
/// The synthetic cast (docs/00-overview/personas.md) as tenants, facilities and one membership per role (MDV-P0-027).
/// Fixed ids make re-seeding idempotent; every name ends in "(synthetic)". Never runs outside Development/Staging/Test.
/// </summary>
public static class SyntheticSeed
{
    public const string Marker = " (synthetic)";

    static readonly string[] AllowedEnvironments = ["Development", "Staging", "Test"];

    // Recognisable fixed ids: 5e1d<tenant>-<kind>-…-<n>; kind 0 tenant, 1 facility, 2 user, 3 membership, 4 patient, 5 identifier, 6 payer, 7 schedule resource, 8 schedule template.
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

    // Patient-role memberships are portal users; the patient records themselves are CastTenant.Patients.
    public static readonly CastTenant India = Clinic(1, "Iyer Family Clinic", "IN", "centralindia", "Adyar", "Asia/Kolkata",
        ("Dr. Arjun Rao", Role.SoloDoctor),
        ("Dr. S. Menon", Role.HospitalDoctor),
        ("A. Joseph", Role.Receptionist),
        ("S. Thomas RN", Role.Nurse),
        ("Priya N.", Role.CareCoordinator),
        ("Meera Iyer", Role.Patient),
        ("K. Lakshmi", Role.PracticeAdmin), // not in the design cast; Dr. Rao already holds the doctor membership at Adyar
        ("R. Varghese", Role.HospitalIt)) with
    {
        Patients =
        [
            Person(1, 1, "IN", "DQ-10-4482", "Meera Iyer", null, new(1972, 3, 12), SexAtBirth.Female, "+919000000101",
                new PatientAddress { Line1 = "4 Synthetic Street", City = "Chennai", District = "Chennai", Region = "Tamil Nadu", PostalCode = "600020" },
                new PayerCoverage { Kind = PayerKind.SelfPay },
                (IdentifierScheme.AadhaarLast4, "0101")),
            Person(1, 2, "IN", "DQ-10-4483", "Deepak Nair", null, new(1979, 5, 20), SexAtBirth.Male, "+919000000102",
                new PatientAddress { Line1 = "7 Synthetic Lane", City = "Kochi", District = "Ernakulam", Region = "Kerala", PostalCode = "682001" },
                new PayerCoverage { Kind = PayerKind.PrivateInsurance, PayerName = "Synthetic TPA" + Marker, MemberNumber = "SYN-TPA-0102" },
                (IdentifierScheme.Abha, "91000000000102"), (IdentifierScheme.AbhaAddress, "deepak.nair.synthetic@sbx")),
        ],
        // The P-04 design day: Dr. Rao in Room 3 with 20-minute slots, Dr. Menon "Not in today · Mon–Wed", a procedure room.
        Resources = [Room(1, 1, "Procedure room")],
        Templates =
        [
            Week(1, 1, Id(1, 2, 1), CalendarKind.Doctor, 20, Days(DayOfWeek.Monday, DayOfWeek.Saturday), "Room 3", (9, 13), (17, 20)),
            Week(1, 2, Id(1, 2, 2), CalendarKind.Doctor, 20, Days(DayOfWeek.Monday, DayOfWeek.Wednesday), "Room 2", (10, 14)),
            Week(1, 3, Id(1, 7, 1), CalendarKind.Resource, 30, Days(DayOfWeek.Monday, DayOfWeek.Saturday), null, (10, 13)),
        ],
    };

    public static readonly CastTenant Uae = Clinic(2, "Al Noor Family Clinic", "AE", "uaenorth", "Abu Dhabi", "Asia/Dubai",
        ("Dr. Omar Haddad", Role.SoloDoctor),
        ("Fatima Al Mansoori", Role.Patient)) with
    {
        Patients =
        [
            // EID fails its Luhn check by construction (pnpm check:synthetic).
            Person(2, 1, "AE", "AN-01-0001", "Fatima Al Mansoori", "فاطمة المنصوري", new(1988, 4, 2), SexAtBirth.Female, "+971500000201",
                new PatientAddress { Line1 = "Villa 1 (synthetic)", Line2 = "Al Reem", City = "Abu Dhabi", Region = "AUH" },
                new PayerCoverage { Kind = PayerKind.PrivateInsurance, PayerName = "Thiqa", MemberNumber = "SYN-THQ-0201" },
                (IdentifierScheme.EmiratesId, "784-1988-1234567-1")),
        ],
        Templates = [Week(2, 1, Id(2, 2, 1), CalendarKind.Doctor, 20, Days(DayOfWeek.Monday, DayOfWeek.Friday), "Room 1", (9, 13), (16, 20))],
    };

    static ScheduleResource Room(int t, int n, string name) =>
        new() { Id = Id(t, 7, n), TenantId = Id(t, 0, 1), FacilityId = Id(t, 1, 1), Name = name + Marker, CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };

    static DayOfWeek[] Days(DayOfWeek from, DayOfWeek to) => [.. Enumerable.Range((int)from, to - from + 1).Select(d => (DayOfWeek)d)];

    static ScheduleTemplate Week(int t, int n, Guid calendar, CalendarKind kind, int slotMinutes, DayOfWeek[] days, string? room, params (int From, int To)[] sessions) => new()
    {
        Id = Id(t, 8, n), TenantId = Id(t, 0, 1), FacilityId = Id(t, 1, 1), CalendarId = calendar, CalendarKind = kind, SlotMinutes = slotMinutes,
        Hours = [.. days.SelectMany(d => sessions.Select(s => new WorkingHours { Weekday = d, Start = new(s.From, 0), End = new(s.To, 0), Room = room }))],
        VisitLengths = [new VisitLength { VisitType = VisitType.NewConsultation, Minutes = 2 * slotMinutes }],
        ChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    static CastPatient Person(int t, int n, string country, string mrn, string name, string? arabicName, DateOnly dob, SexAtBirth sex, string mobile,
        PatientAddress address, PayerCoverage payer, params (IdentifierScheme Scheme, string Value)[] ids)
    {
        var tenantId = Id(t, 0, 1);
        var patient = new Patient
        {
            Id = Id(t, 4, n), TenantId = tenantId, FacilityId = Id(t, 1, 1), Mrn = mrn, CountryCode = country,
            FullName = name + Marker, ArabicName = arabicName is null ? null : arabicName + Marker, DateOfBirth = dob, SexAtBirth = sex,
            Mobile = mobile, Address = address, Status = RegistrationStatus.Complete,
            RegisteredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var identifiers = ids.Select((id, i) => new PatientIdentifier
        {
            Id = Id(t, 5, n * 10 + i), TenantId = tenantId, PatientId = patient.Id, Scheme = id.Scheme,
            LookupValue = PatientIdentifier.IsLookup(id.Scheme) ? id.Value : null,
            DisplayValue = PatientIdentifier.IsLookup(id.Scheme) ? null : id.Value,
        }).ToList();
        return new(patient, identifiers, new PayerCoverage
        {
            Id = Id(t, 6, n), TenantId = tenantId, PatientId = patient.Id, Kind = payer.Kind, PayerName = payer.PayerName, MemberNumber = payer.MemberNumber,
        });
    }

    public static IReadOnlyList<CastTenant> Tenants(bool includeUae) => includeUae ? [India, Uae] : [India];

    /// <summary>Throws unless the environment is explicitly Development, Staging or Test; a missing value is refused too.</summary>
    public static void EnsureNonProduction(string? environment)
    {
        if (!AllowedEnvironments.Contains(environment, StringComparer.Ordinal))
            throw new InvalidOperationException($"Synthetic seeding refused: environment '{environment}' is not Development, Staging or Test.");
    }

    /// <summary>
    /// Creates the Always Encrypted keys (wrapped by <paramref name="keys"/>, which must be registered), applies every module's
    /// migrations, then adds any missing cast rows. Existing rows are left as they are.
    /// </summary>
    public static async Task SeedAsync(string connectionString, string? environment, bool includeUae, LocalKeyStoreProvider keys, CancellationToken ct = default)
    {
        EnsureNonProduction(environment);

        await using (var tenancy = Tenancy(connectionString, null)) await tenancy.Database.MigrateAsync(ct);
        await using (var audit = new AuditDbContext(Options<AuditDbContext>(connectionString, null), new FixedTenant(null)))
            await audit.Database.MigrateAsync(ct);
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connectionString).Options);
        await identity.Database.MigrateAsync(ct);
        await using (var admin = new SqlConnection(connectionString))
        {
            await admin.OpenAsync(ct);
            await ColumnEncryption.EnsureKeysAsync(admin, LocalKeyStoreProvider.ProviderName, LocalKeyStoreProvider.KeyPath, keys, ct);
        }
        await using (var patients = Patients(connectionString, null)) await patients.Database.MigrateAsync(ct);
        await using (var scheduling = Scheduling(connectionString, null)) await scheduling.Database.MigrateAsync(ct);

        foreach (var c in Tenants(includeUae))
        {
            // Writes run inside the tenant, so the RLS block predicates apply exactly as they do for the API.
            await using var db = Tenancy(connectionString, c.Tenant.Id);
            if (await db.Tenants.FindAsync([c.Tenant.Id], ct) is null) db.Tenants.Add(c.Tenant);
            if (await db.Facilities.FindAsync([c.Facility.Id], ct) is null) db.Facilities.Add(c.Facility);
            var existing = await db.Memberships.Select(m => m.Id).ToListAsync(ct);
            db.Memberships.AddRange(c.Members.Where(m => !existing.Contains(m.Membership.Id)).Select(m => m.Membership));
            await db.SaveChangesAsync(ct);

            // Accounts for the development sign-in provider (Development only); the subject is the Medvya user id.
            var users = await identity.Users.Select(u => u.Id).ToListAsync(ct);
            identity.Users.AddRange(c.Members.Where(m => !users.Contains(m.UserId)).Select(m => new StaffUser
            {
                Id = m.UserId, Issuer = IdentityProviders.Development, Subject = m.UserId.ToString(), DisplayName = m.Name,
            }));
            await identity.SaveChangesAsync(ct);

            await using var pdb = Patients(connectionString, c.Tenant.Id);
            var seeded = await pdb.Patients.Select(p => p.Id).ToListAsync(ct);
            foreach (var p in c.Patients.Where(p => !seeded.Contains(p.Patient.Id)))
            {
                pdb.Patients.Add(p.Patient);
                pdb.Identifiers.AddRange(p.Identifiers);
                pdb.PayerCoverages.Add(p.Payer);
            }
            await pdb.SaveChangesAsync(ct);

            await using var sdb = Scheduling(connectionString, c.Tenant.Id);
            var resources = await sdb.Resources.Select(r => r.Id).ToListAsync(ct);
            var templates = await sdb.Templates.Select(t => t.Id).ToListAsync(ct);
            sdb.Resources.AddRange(c.Resources.Where(r => !resources.Contains(r.Id)));
            sdb.Templates.AddRange(c.Templates.Where(t => !templates.Contains(t.Id)));
            await sdb.SaveChangesAsync(ct);
        }
    }

    static PatientsDbContext Patients(string connectionString, Guid? tenantId) =>
        new(Options<PatientsDbContext>(PatientsModule.WithColumnEncryption(connectionString), tenantId), new FixedTenant(tenantId));

    static SchedulingDbContext Scheduling(string connectionString, Guid? tenantId) =>
        new(Options<SchedulingDbContext>(connectionString, tenantId), new FixedTenant(tenantId));

    sealed class FixedTenant(Guid? id) : ICurrentTenant { public Guid? TenantId => id; }

    static TenancyDbContext Tenancy(string connectionString, Guid? tenantId) =>
        new(Options<TenancyDbContext>(connectionString, tenantId), new FixedTenant(tenantId));

    static DbContextOptions<T> Options<T>(string connectionString, Guid? tenantId) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlServer(connectionString)
            .AddInterceptors(new TenantSessionInterceptor(new FixedTenant(tenantId))).Options;
}
