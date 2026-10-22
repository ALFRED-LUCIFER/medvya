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

    PatientRegistry Registry(Guid tenant, PatientsDbContext? db = null, IAuditLog? audit = null, Guid? at = null) =>
        new(db ?? sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new FixedFacility(at ?? facility), new FixedUser(actor),
            audit ?? new AuditLog(sql.Audit(tenant)), TimeProvider.System);

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

    static RegisterPatient UaeQuick(Guid facility) => UaeBase with { FacilityId = facility, Quick = true, DateOfBirth = NextDob() };

    [Fact]
    public async Task FullRegistrationAssignsFacilityScopedMrnsAndIsAudited()
    {
        var first = await Registry(tenantA).RegisterAsync(IndiaFull(facility));
        var second = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { Identifiers = [] });
        var otherFacility = await Registry(tenantA).RegisterAsync(IndiaFull(Guid.NewGuid()) with { Identifiers = [] });

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
    public async Task QuickRegistrationStaysIncompleteUntilAddressAndPayerAreAdded()
    {
        var quick = await Registry(tenantA).RegisterAsync(UaeQuick(facility));
        Assert.Equal(RegistrationStatus.Incomplete, quick.Status);

        var address = new PatientAddress { Line1 = "Villa 1", Line2 = "Al Reem", Region = "AUH" };
        Assert.Equal(RegistrationStatus.Incomplete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, address, null));
        Assert.Equal(RegistrationStatus.Complete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, null, new(PayerKind.SelfPay)));

        var record = await Registry(tenantA).GetAsync(quick.PatientId);
        Assert.Equal((RegistrationStatus.Complete, "AUH", PayerKind.SelfPay), (record!.Patient.Status, record.Patient.Address!.Region, record.Payer!.Kind));
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
    }

    [Fact]
    public async Task AnotherFacilitysPatientIsNeitherListedNorOpened()
    {
        var elsewhere = await Registry(tenantA).RegisterAsync(IndiaFull(Guid.NewGuid()) with { FullName = "Rahul Menon (synthetic)" });
        Assert.Empty((await Search(tenantA, "IN", "Rahul")).Items);
        Assert.Empty((await Search(tenantA, "IN", "91-0000-0000-0002")).Items);
        Assert.Null(await Registry(tenantA).GetAsync(elsewhere.PatientId));
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
        await Registry(tenantA).RegisterAsync(UaeQuick(Guid.NewGuid()));

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
        var registered = await Registry(tenantA).RegisterAsync(IndiaFull(Guid.NewGuid()) with { DateOfBirth = new DateOnly(1980, 1, 1) });
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

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }
}
