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
/// MDV-P1-013/014/015 and MDV-P0-009 against real SQL Server: registration, audited reads, exact lookups through Always Encrypted,
/// dynamic data masking and tenant isolation. Synthetic data only; IDs fail their checksums by construction.
/// </summary>
public class PatientRegistryTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), actor = Guid.NewGuid();

    const string Eid = "784-1990-1234567-1";
    const string Abha = "91000000000002";

    PatientRegistry Registry(Guid tenant, PatientsDbContext? db = null, IAuditLog? audit = null) =>
        new(db ?? sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new FixedUser(actor), audit ?? new AuditLog(sql.Audit(tenant)), TimeProvider.System);

    static RegisterPatient IndiaFull(Guid facility) => IndiaBase with
    {
        FacilityId = facility, Email = "patient@example.test",
        Identifiers = [new(IdentifierScheme.Abha, "91-0000-0000-0002"), new(IdentifierScheme.AadhaarLast4, "2222")],
        Address = new PatientAddress { Line1 = "12 Test Street", City = "Kochi", District = "Ernakulam", Region = "Kerala", PostalCode = "682001" },
        Payer = new(PayerKind.PrivateInsurance, "TPA (synthetic)", MemberNumber: "SYN-0001"),
    };

    static RegisterPatient UaeQuick(Guid facility) => UaeBase with { FacilityId = facility, Quick = true };

    [Fact]
    public async Task FullRegistrationAssignsFacilityScopedMrnsAndIsAudited()
    {
        var facility = Guid.NewGuid();
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
        Assert.Equal(["patient.register", "patient.read"], actions);
    }

    [Fact]
    public async Task QuickRegistrationStaysIncompleteUntilAddressAndPayerAreAdded()
    {
        var quick = await Registry(tenantA).RegisterAsync(UaeQuick(Guid.NewGuid()));
        Assert.Equal(RegistrationStatus.Incomplete, quick.Status);

        var address = new PatientAddress { Line1 = "Villa 1", Line2 = "Al Reem", Region = "AUH" };
        Assert.Equal(RegistrationStatus.Incomplete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, address, null));
        Assert.Equal(RegistrationStatus.Complete, await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, null, new(PayerKind.SelfPay)));

        var record = await Registry(tenantA).GetAsync(quick.PatientId);
        Assert.Equal((RegistrationStatus.Complete, "AUH", PayerKind.SelfPay), (record!.Patient.Status, record.Patient.Address!.Region, record.Payer!.Kind));
    }

    [Fact]
    public async Task ExactLookupByEmiratesIdAndAbhaRunsAsTheAppRoleThroughEfCore()
    {
        // The application's own database role, not the administrator: proves the grants needed for encrypted parameters.
        await using (var db = await sql.PatientsAs("app_user", tenantA))
        {
            await Registry(tenantA, db).RegisterAsync(UaeQuick(Guid.NewGuid()));
        }
        await Registry(tenantA).RegisterAsync(IndiaFull(Guid.NewGuid()));

        await using (var db = await sql.PatientsAs("app_user", tenantA))
        {
            var byEid = await Registry(tenantA, db).FindByIdentifierAsync("AE", IdentifierScheme.EmiratesId, "784199012345671");
            Assert.Equal(Eid, byEid!.Identifiers.Single().LookupValue);
        }
        await using (var db = await sql.PatientsAs("app_user", tenantA))
        {
            var byAbha = await Registry(tenantA, db).FindByIdentifierAsync("IN", IdentifierScheme.Abha, "91-0000-0000-0002");
            Assert.Equal("Kerala", byAbha!.Patient.Address!.Region);
        }
        Assert.Null(await Registry(tenantA).FindByIdentifierAsync("AE", IdentifierScheme.EmiratesId, "784-1990-7654321-0"));
        await Assert.ThrowsAsync<PatientValidationException>(() => Registry(tenantA).FindByIdentifierAsync("AE", IdentifierScheme.Passport, "SYN12345"));
    }

    [Fact]
    public async Task AnotherTenantNeverFindsThePatientEvenWithQueryFiltersOff()
    {
        await Registry(tenantA).RegisterAsync(UaeQuick(Guid.NewGuid()));
        Assert.Null(await Registry(tenantB).FindByIdentifierAsync("AE", IdentifierScheme.EmiratesId, Eid));

        await using var db = sql.Patients(tenantB);
        Assert.Empty(await db.Patients.IgnoreQueryFilters().Where(p => p.TenantId == tenantA).ToListAsync()); // row-level security
    }

    [Fact]
    public async Task ARegistrationThatCannotBeAuditedIsNotSaved()
    {
        var facility = Guid.NewGuid();
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
        var registered = await Registry(tenantA).RegisterAsync(IndiaFull(Guid.NewGuid()));
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

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }
}
