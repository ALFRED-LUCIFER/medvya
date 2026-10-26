using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Patients.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-013/014/015/018 and MDV-P0-009 against real SQL Server: registration, audited writes, directory and exact search through
/// Always Encrypted, dynamic data masking, facility scope and tenant isolation. Synthetic data only; IDs fail their checksums by construction.
/// Read auditing is the endpoint's job (one event per request): PatientEndpointTests.
/// </summary>
public class PatientRegistryTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), actor = Guid.NewGuid(), facility = Guid.NewGuid();

    const string Eid = "784-1990-1234567-1";
    const string Abha = "91000000000002";

    PatientRegistry Registry(Guid tenant, PatientsDbContext? db = null, IAuditLog? audit = null, Guid? at = null, TimeProvider? clock = null, string zone = "Asia/Kolkata") =>
        new(db ?? sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new FixedFacility(at ?? facility), new FixedUser(actor),
            audit ?? new AuditLog(sql.Audit(tenant)), clock ?? TimeProvider.System, new FacilityZone(zone));

    Task<PatientSearchResult> Search(Guid tenant, string country, string? query, PatientsDbContext? db = null, DirectoryFilter filter = DirectoryFilter.All, int page = 1) =>
        Registry(tenant, db).SearchAsync(new(country, query, filter, page));

    // A different date of birth per registration, so the duplicate check (MDV-P1-016) sees different people.
    static int born;
    static DateOnly NextDob() => new DateOnly(1950, 1, 1).AddDays(Interlocked.Increment(ref born));

    static RegisterPatient IndiaFull(Guid facility) => IndiaBase with
    {
        FacilityId = facility, Email = "patient@example.test", DateOfBirth = NextDob(),
        Identifiers = [new(IdentifierScheme.Abha, "91-0000-0000-0002"), new(IdentifierScheme.AadhaarLast4, "2222")],
        Address = new PatientAddress { Line1 = "12 Test Street", City = "Kochi", District = "Ernakulam", Region = "Kerala", PostalCode = "682001" },
        Payer = new(PayerKind.PrivateInsurance, "TPA (synthetic)", MemberNumber: "SYN-0001"),
    };

    static RegisterPatient UaeQuick(Guid facility) => UaeBase with { FacilityId = facility, Quick = true, DateOfBirth = NextDob(), EmergencyContact = null };

    [Fact]
    public async Task FullRegistrationAssignsFacilityScopedMrnsAndIsAudited()
    {
        var first = await Registry(tenantA).RegisterAsync(IndiaFull(facility));
        var second = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { Identifiers = [] });
        var other = Guid.NewGuid();
        var otherFacility = await Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { Identifiers = [] });

        Assert.Equal(("000001", "000002", "000001"), (first.Mrn, second.Mrn, otherFacility.Mrn));
        Assert.Equal((RegistrationStatus.Complete, actor), (first.Status, first.RegisteredBy!.Value));

        var record = await Registry(tenantA).GetAsync(first.PatientId);
        Assert.NotNull(record);
        Assert.Equal(("+919000000002", "Kerala", "SYN-0001"), (record.Patient.Mobile, record.Patient.Address!.Region, record.Payer!.MemberNumber));
        Assert.Equal(["2222", Abha], record.Identifiers.Select(i => i.LookupValue ?? i.DisplayValue).Order());

        await using var audit = sql.Audit(tenantA);
        var actions = await audit.Events.Where(e => e.PatientId == first.PatientId).OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
        Assert.Equal(["patient.register"], actions); // the read is audited once, by its endpoint
    }

    [Fact]
    public async Task QuickRegistrationStaysIncompleteUntilAddressPayerAndEmergencyContactAreAdded()
    {
        var quick = await Registry(tenantA).RegisterAsync(UaeQuick(facility));
        Assert.Equal(RegistrationStatus.Incomplete, quick.Status);

        var address = new PatientAddress { Line1 = "Villa 1", Line2 = "Al Reem", Region = "AUH" };
        Assert.Equal(RegistrationStatus.Incomplete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, address, null));
        Assert.Equal(RegistrationStatus.Incomplete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, null, new(PayerKind.SelfPay)));
        // MDV-P1-020: the emergency contact is the third thing the completion task waits for.
        Assert.Equal(RegistrationStatus.Complete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, null, null, UaeContact));

        var record = await Registry(tenantA).GetAsync(quick.PatientId);
        Assert.Equal((RegistrationStatus.Complete, "AUH", PayerKind.SelfPay), (record!.Patient.Status, record.Patient.Address!.Region, record.Payer!.Kind));
    }

    /// <summary>
    /// SAFETY (MDV-P1-020): the guardian rule takes the registration day on the facility's calendar. At 20:00 UTC on 20 Oct it is
    /// already 21 Oct in Kochi, so a patient born 21 Oct 2008 is 18 there (no guardian needed) but still 17 in UTC.
    /// </summary>
    [Fact]
    public async Task TheGuardianRuleUsesTheFacilitysDay()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero));
        var turning18 = IndiaFull(facility) with { DateOfBirth = new DateOnly(2008, 10, 21), Identifiers = [], FullName = "Anjali Menon (synthetic)" };

        var refused = await Assert.ThrowsAsync<PatientValidationException>(() => Registry(tenantA, clock: clock, zone: "Etc/UTC").RegisterAsync(turning18));
        Assert.Equal([new ValidationError("guardian", "required_for_minor")], refused.Errors);
        await using (var db = sql.Patients(tenantA))
            Assert.False(await db.Patients.AnyAsync(p => p.FullName == "Anjali Menon (synthetic)")); // nothing saved

        var adult = await Registry(tenantA, clock: clock, zone: "Asia/Kolkata").RegisterAsync(turning18);
        Assert.Equal(RegistrationStatus.Complete, adult.Status);
    }

    /// <summary>Contacts are rows to call, never users: registering them creates no account, membership or portal access.</summary>
    [Fact]
    public async Task ContactsAreStoredForTheRecordAndNeverBecomeUsers()
    {
        int users, memberships;
        await using (var identity = sql.Identity()) users = await identity.Users.CountAsync();
        await using (var tenancy = sql.Context(tenantA)) memberships = await tenancy.Memberships.IgnoreQueryFilters().CountAsync();

        var guardian = new ContactInput("Ravi Nair (synthetic)", RelationshipKind.Parent, "+91 90000 00010");
        var child = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { DateOfBirth = new DateOnly(2016, 5, 1), Identifiers = [], Guardian = guardian });
        var record = await Registry(tenantA).GetAsync(child.PatientId);
        Assert.Equal([("Lakshmi Nair (synthetic)", RelationshipKind.Spouse, "+919000000009", true, false), ("Ravi Nair (synthetic)", RelationshipKind.Parent, "+919000000010", false, true)],
            record!.Relationships.Select(r => (r.Name, r.Relationship, r.Phone, r.IsEmergencyContact, r.IsGuardian)));

        await using (var identity = sql.Identity()) Assert.Equal(users, await identity.Users.CountAsync());
        await using (var tenancy = sql.Context(tenantA)) Assert.Equal(memberships, await tenancy.Memberships.IgnoreQueryFilters().CountAsync());

        // Tenant isolation, both layers: another tenant sees none of them even with the EF query filters off (row-level security).
        await using var other = sql.Patients(tenantB);
        Assert.Empty(await other.Relationships.IgnoreQueryFilters().Where(r => r.PatientId == child.PatientId).ToListAsync());
    }

    [Fact]
    public async Task ExactSearchByEmiratesIdAndAbhaRunsAsTheAppRoleThroughEfCore()
    {
        // The application's own database role, not the administrator: proves the grants needed for encrypted parameters.
        RegistrationResult uae;
        await using (var db = await sql.PatientsAs("app_user", tenantA)) uae = await Registry(tenantA, db).RegisterAsync(UaeQuick(facility));
        var india = await Registry(tenantA).RegisterAsync(IndiaFull(facility));

        await using (var db = await sql.PatientsAs("app_user", tenantA))
        {
            var byEid = await Search(tenantA, "AE", "784199012345671", db);
            Assert.Equal((SearchKind.Identifier, uae.PatientId), (byEid.MatchedOn, Assert.Single(byEid.Items).Id));
        }
        await using (var db = await sql.PatientsAs("app_user", tenantA))
            Assert.Equal(india.PatientId, Assert.Single((await Search(tenantA, "IN", "91-0000-0000-0002", db)).Items).Id);
        Assert.Empty((await Search(tenantA, "AE", "784-1990-7654321-0")).Items);
        // A partial ID never matches: it is refused before any query runs.
        await Assert.ThrowsAsync<PatientValidationException>(() => Search(tenantA, "AE", "784-1990-1234567"));
    }

    [Fact]
    public async Task SearchRowsShowOnlyInitialsAgeSexAndTheMrnTail()
    {
        var registered = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Kavya Lakshmi Iyer (synthetic)", DateOfBirth = new DateOnly(1980, 1, 1), Identifiers = [] });
        var result = await Search(tenantA, "IN", "kavya");

        Assert.Equal(SearchKind.Name, result.MatchedOn);
        var row = Assert.Single(result.Items);
        var age = CountryRules.India.Today(DateTimeOffset.UtcNow).Year - 1980; // born 1 Jan 1980
        Assert.Equal(new PatientSearchRow(registered.PatientId, "KI", age, false, SexAtBirth.Female, registered.Mrn[^4..], RegistrationStatus.Complete), row);
        // The row type itself has no name, phone, date of birth or identifier to leak.
        Assert.Equal(["AgeEstimated", "AgeYears", "Id", "Initials", "MrnTail", "SexAtBirth", "Status"], typeof(PatientSearchRow).GetProperties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task PhoneAndMrnMatchExactlyAndNamesMatchContainedText()
    {
        var meera = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Meera Iyer (synthetic)", Mobile = "+91 90000 00101", Identifiers = [] });
        await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Meera_Rao (synthetic)", Mobile = "+91 90000 00102", Identifiers = [] });
        await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Sara Rao (synthetic)", Mobile = "+91 90000 00103", Identifiers = [] });

        Assert.Equal(meera.PatientId, Assert.Single((await Search(tenantA, "IN", "090000 00101")).Items).Id);
        Assert.Equal(meera.PatientId, Assert.Single((await Search(tenantA, "IN", meera.Mrn.TrimStart('0'))).Items).Id);
        Assert.Equal(2, (await Search(tenantA, "IN", "MEERA")).Total);
        Assert.Single((await Search(tenantA, "IN", "a_r")).Items); // "_" is matched literally, not as a wildcard ("Sara Rao")
        await Assert.ThrowsAsync<PatientValidationException>(() => Search(tenantA, "IN", "90000 001")); // part of a phone number
    }

    [Fact]
    public async Task DirectoryFiltersIncompleteAndRecentRegistrationsAndPages()
    {
        var quick = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { Quick = true, Address = null, Payer = null, Identifiers = [] });
        for (var i = 0; i < PatientSearch.PageSize; i++) await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { Identifiers = [] });

        var all = await Search(tenantA, "IN", null);
        Assert.Equal((SearchKind.Directory, PatientSearch.PageSize + 1, PatientSearch.PageSize), (all.MatchedOn, all.Total, all.Items.Count));
        Assert.Single((await Search(tenantA, "IN", null, page: 2)).Items);
        Assert.Equal(quick.PatientId, Assert.Single((await Search(tenantA, "IN", null, filter: DirectoryFilter.Incomplete)).Items).Id);
        Assert.Equal(PatientSearch.PageSize + 1, (await Search(tenantA, "IN", null, filter: DirectoryFilter.Recent)).Total);
        await Assert.ThrowsAsync<PatientValidationException>(() => Search(tenantA, "IN", null, page: 0));
        // Paging is for the desk, not an export: past 50 pages (1,000 rows) it is refused.
        await Assert.ThrowsAsync<PatientValidationException>(() => Search(tenantA, "IN", null, page: PatientSearch.MaxPage + 1));
        Assert.Equal(50, PatientSearch.MaxPage);
    }

    [Fact]
    public async Task AnotherFacilitysPatientIsNeitherListedNorOpened()
    {
        var other = Guid.NewGuid();
        var elsewhere = await Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { FullName = "Rahul Menon (synthetic)" });
        Assert.Empty((await Search(tenantA, "IN", "Rahul")).Items);
        Assert.Empty((await Search(tenantA, "IN", "91-0000-0000-0002")).Items);
        Assert.Null(await Registry(tenantA).GetAsync(elsewhere.PatientId));
    }

    /// <summary>Writes are facility-scoped like reads: a desk can neither register into nor complete another facility's record.</summary>
    [Fact]
    public async Task AnotherFacilitysPatientCannotBeCompletedOrRegisteredInto()
    {
        var other = Guid.NewGuid();
        var elsewhere = await Registry(tenantA, at: other).RegisterAsync(UaeQuick(other));

        var address = new PatientAddress { Line1 = "Villa 1", Line2 = "Al Reem", Region = "AUH" };
        Assert.Null(await Registry(tenantA).CompleteRegistrationAsync(elsewhere.PatientId, address, new(PayerKind.SelfPay)));
        await using (var db = sql.Patients(tenantA))
        {
            var unchanged = await db.Patients.SingleAsync(p => p.Id == elsewhere.PatientId);
            Assert.Equal((RegistrationStatus.Incomplete, (PatientAddress?)null), (unchanged.Status, unchanged.Address));
            Assert.False(await db.PayerCoverages.AnyAsync(c => c.PatientId == elsewhere.PatientId));
        }
        await using (var audit = sql.Audit(tenantA))
            Assert.False(await audit.Events.AnyAsync(e => e.PatientId == elsewhere.PatientId && e.Action == "patient.update")); // nothing was changed

        // A command naming another facility than the one the desk works at is refused before anything is written.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Registry(tenantA).RegisterAsync(UaeQuick(other)));
        await using var check = sql.Patients(tenantA);
        Assert.Equal(1, await check.Patients.CountAsync(p => p.FacilityId == other));
    }

    [Fact]
    public async Task AnotherTenantNeverFindsThePatientEvenWithQueryFiltersOff()
    {
        await Registry(tenantA).RegisterAsync(UaeQuick(facility));
        Assert.Empty((await Search(tenantB, "AE", Eid)).Items);

        await using var db = sql.Patients(tenantB);
        Assert.Empty(await db.Patients.IgnoreQueryFilters().Where(p => p.TenantId == tenantA).ToListAsync()); // row-level security
    }

    [Fact]
    public async Task ARegistrationThatCannotBeAuditedIsNotSaved()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Registry(tenantA, audit: new ThrowingLog()).RegisterAsync(IndiaFull(facility)));

        await using var db = sql.Patients(tenantA);
        Assert.False(await db.Patients.AnyAsync(p => p.FacilityId == facility));
        Assert.Equal("000001", (await Registry(tenantA).RegisterAsync(IndiaFull(facility))).Mrn); // the MRN counter rolled back too
    }

    [Fact]
    public async Task AnAdministratorWithoutTheKeySeesOnlyCiphertext()
    {
        var other = Guid.NewGuid();
        await Registry(tenantA, at: other).RegisterAsync(UaeQuick(other));

        // Column Encryption Setting off: what SSMS or a DBA without access to the column master key gets.
        await using var conn = await sql.OpenAs("dbo", tenantA);
        await using var cmd = new SqlCommand("SELECT TOP 1 LookupValue FROM patients.Identifiers WHERE Scheme = N'EmiratesId'", conn);
        var value = await cmd.ExecuteScalarAsync();
        var bytes = Assert.IsType<byte[]>(value);
        Assert.DoesNotContain("784", System.Text.Encoding.Unicode.GetString(bytes), StringComparison.Ordinal);

        // The protected-column list (data-classification.md), kept deliberately small.
        await using var columns = new SqlCommand("""
            SELECT STRING_AGG(OBJECT_NAME(object_id) COLLATE DATABASE_DEFAULT + '.' + name COLLATE DATABASE_DEFAULT + ':' + encryption_type_desc COLLATE DATABASE_DEFAULT, ',') WITHIN GROUP (ORDER BY OBJECT_NAME(object_id), name)
            FROM sys.columns WHERE encryption_type IS NOT NULL
            """, conn);
        Assert.Equal("Identifiers.DisplayValue:RANDOMIZED,Identifiers.LookupValue:DETERMINISTIC,PayerCoverages.MemberNumber:DETERMINISTIC", await columns.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ALikeSearchOnAnEncryptedColumnIsRejected()
    {
        await using var db = sql.Patients(tenantA);
        var prefix = "784%";
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Identifiers.Where(i => EF.Functions.Like(i.LookupValue, prefix)).ToListAsync());
        Assert.Contains("Encryption scheme mismatch", ex.Message, StringComparison.Ordinal); // deterministic encryption supports equality only
    }

    [Fact]
    public async Task ReportingSeesMaskedDemographicsWhileTheAppSeesClearValues()
    {
        var other = Guid.NewGuid();
        var registered = await Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { DateOfBirth = new DateOnly(1980, 1, 1) });
        const string Query = "SELECT FullName, Mobile, Email, DateOfBirth, Address_PostalCode FROM patients.Patients WHERE Id = @id";

        async Task<object[]> Row(string user)
        {
            await using var conn = await sql.OpenAs(user, tenantA);
            await using var cmd = new SqlCommand(Query, conn);
            cmd.Parameters.AddWithValue("@id", registered.PatientId);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            return values;
        }

        var masked = await Row("reporting_user");
        Assert.Equal(["xxxx", "xxxx", "pXXX@XXXX.com", new DateTime(1900, 1, 1), "xxxx"], masked);

        var clear = await Row("app_user");
        Assert.Equal(["Test Patient (synthetic)", "+919000000002", "patient@example.test", new DateTime(1980, 1, 1), "682001"], clear);
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    internal sealed class FixedFacility(Guid? id) : ICurrentFacility { public Guid? FacilityId => id; }

    /// <summary>Every facility in this IANA time zone (the registration day is taken there, MDV-P1-020).</summary>
    internal sealed class FacilityZone(string zone) : Medvya.Tenancy.Application.IFacilityDirectory
    {
        public Task<Medvya.Tenancy.Application.FacilityInfo?> FindAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Medvya.Tenancy.Application.FacilityInfo?>(new(facilityId, "Synthetic facility", zone));
        public Task<Medvya.Tenancy.Domain.Role?> ActiveRoleAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ActiveDoctorsAsync(Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
        public Task WriteAsync(AuditEvent auditEvent, System.Data.Common.DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }
}
