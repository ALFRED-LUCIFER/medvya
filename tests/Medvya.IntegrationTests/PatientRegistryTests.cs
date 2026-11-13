using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure;
using Medvya.Patients.Infrastructure.Persistence;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using static Medvya.IntegrationTests.PatientRulesTests;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-013/014/015/018 and MDV-P0-009 against real SQL Server: registration, audited writes, directory and exact search through
/// Always Encrypted, dynamic data masking, facility scope and tenant isolation. Synthetic data only; IDs fail their checksums by construction.
/// Read auditing is the endpoint's job (one event per request): PatientEndpointTests.
/// </summary>
public class PatientRegistryTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid(), actor = Guid.NewGuid(), facility = Guid.NewGuid();

    const string Eid = "784-1990-1234567-1";
    const string Abha = "91000000000002";

    PatientRegistry Registry(Guid tenant, PatientsDbContext? db = null, IAuditLog? audit = null, Guid? at = null, TimeProvider? clock = null, string zone = "Asia/Kolkata") =>
        new(db ?? sql.Patients(tenant), new PostgresFixture.FixedTenant(tenant), new FixedFacility(at ?? facility), new FixedUser(actor),
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
    public async Task FullRegistrationAssignsOrganisationWideMrnsAndIsAudited()
    {
        var first = await Registry(tenantA).RegisterAsync(IndiaFull(facility));
        var second = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { Identifiers = [] });
        var other = Guid.NewGuid();
        var otherFacility = await Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { Identifiers = [] });

        Assert.Equal(("000001", "000002", "000003"), (first.Mrn, second.Mrn, otherFacility.Mrn)); // one sequence for both locations (MDV-P1-072)
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
    public async Task ACompletionFromAnOlderReadOfTheRecordIsRefusedAndTheNewerOneStays()
    {
        // #889: two desks open the same record, both complete it. The version each sends is the one it read.
        var quick = await Registry(tenantA).RegisterAsync(UaeQuick(facility));
        var seen = (await Registry(tenantA).GetAsync(quick.PatientId))!.Version;
        await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, new PatientAddress { Line1 = "Villa 1", Region = "AUH" }, null, expectedVersion: seen);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, new PatientAddress { Line1 = "Villa 2", Region = "AUH" }, null, expectedVersion: seen));
        var now = (await Registry(tenantA).GetAsync(quick.PatientId))!;
        Assert.Equal("Villa 1", now.Patient.Address!.Line1);
        Assert.NotEqual(seen, now.Version);
        await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, new PatientAddress { Line1 = "Villa 2", Region = "AUH" }, null, expectedVersion: now.Version); // after a reload
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
        // Part of an ID is only a phone fragment: IDs are encrypted, so it finds nothing.
        Assert.Empty((await Search(tenantA, "AE", "784-1990-1234567")).Items);
    }

    [Fact]
    public async Task SearchRowsShowTheFullNameAgeSexAndOnlyTheMrnAndPhoneTails()
    {
        var registered = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Kavya Lakshmi Iyer (synthetic)", DateOfBirth = new DateOnly(1980, 1, 1), Identifiers = [] });
        var result = await Search(tenantA, "IN", "kavya");

        Assert.Equal(SearchKind.Name, result.MatchedOn);
        var row = Assert.Single(result.Items);
        var age = CountryRules.India.Today(DateTimeOffset.UtcNow).Year - 1980; // born 1 Jan 1980
        Assert.Equal(new PatientSearchRow(registered.PatientId, "KI", "Kavya Lakshmi Iyer (synthetic)", age, false, SexAtBirth.Female, registered.Mrn[^4..], row.MobileTail, RegistrationStatus.Complete), row);
        Assert.Equal(4, row.MobileTail.Length);
        // The row type itself has no date of birth, full phone, email or identifier to leak (MDV-AUDIT-1100: the name and the phone tail are shown on purpose).
        Assert.Equal(["AgeEstimated", "AgeYears", "FullName", "Id", "Initials", "MobileTail", "MrnTail", "SexAtBirth", "Status"], typeof(PatientSearchRow).GetProperties().Select(p => p.Name).Order());
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
        // Punctuation is dropped from a name query, never passed on as a wildcard: "a_r" is the word "ar" (in both Meera Iyer and Sara Rao), and "%" alone is too short.
        Assert.Equal(2, (await Search(tenantA, "IN", "a_r")).Items.Count);
        await Assert.ThrowsAsync<PatientValidationException>(() => Search(tenantA, "IN", "%%"));
        await Assert.ThrowsAsync<PatientValidationException>(() => Search(tenantA, "IN", "1234 5678 9012 3456")); // too long to be any of these
    }

    [Fact]
    public async Task TheDeskFindsAPatientByPartOfThePhoneAnAadhaarOrNamesInAnyOrder()
    {
        var meera = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Meera Iyer (synthetic)", Mobile = "+91 90000 00101", AlternatePhone = "+91 90000 07777", Identifiers = [new(IdentifierScheme.AadhaarLast4, "4471")] });
        var sara = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Sara Rao (synthetic)", Mobile = "+91 90000 00103", Identifiers = [new(IdentifierScheme.AadhaarLast4, "5582")] });

        // Part of the mobile (any 4+ digits, any separators) and of the alternate number.
        var byPhone = await Search(tenantA, "IN", "00 0101");
        Assert.Equal((SearchKind.Number, meera.PatientId), (byPhone.MatchedOn, Assert.Single(byPhone.Items).Id));
        Assert.Equal(meera.PatientId, Assert.Single((await Search(tenantA, "IN", "07777")).Items).Id);
        Assert.Equal(2, (await Search(tenantA, "IN", "9000")).Total); // shared digits find both
        // A typed 12-digit Aadhaar and the typed last 4 find the patient; another ending finds the other.
        var byAadhaar = await Search(tenantA, "IN", "9999 8888 4471");
        Assert.Equal((SearchKind.Identifier, meera.PatientId), (byAadhaar.MatchedOn, Assert.Single(byAadhaar.Items).Id));
        Assert.Equal(meera.PatientId, Assert.Single((await Search(tenantA, "IN", "4471")).Items).Id);
        Assert.Equal(sara.PatientId, Assert.Single((await Search(tenantA, "IN", "5582")).Items).Id);
        Assert.Empty((await Search(tenantA, "IN", "9999 8888 1234")).Items);
        // The MRN still matches, by its digits.
        Assert.Contains(sara.PatientId, (await Search(tenantA, "IN", sara.Mrn)).Items.Select(i => i.Id));
        // Names: each word must appear, in any order.
        Assert.Equal(meera.PatientId, Assert.Single((await Search(tenantA, "IN", "iyer meera")).Items).Id);
        Assert.Empty((await Search(tenantA, "IN", "meera rao")).Items);
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

    /// <summary>MDV-P1-072: one chart per person across the organisation's locations, found by name, MRN or phone from another location.</summary>
    [Fact]
    public async Task APatientRegisteredAtAnotherLocationIsFoundAndOpenedAsOneChart()
    {
        var other = Guid.NewGuid();
        var elsewhere = await Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { FullName = "Rahul Menon (synthetic)", Mobile = "+91 90000 04242", Identifiers = [] });

        foreach (var query in new[] { "Rahul", elsewhere.Mrn, "+91 90000 04242" })
            Assert.Equal(elsewhere.PatientId, Assert.Single((await Search(tenantA, "IN", query)).Items).Id);
        var record = await Registry(tenantA).GetAsync(elsewhere.PatientId);
        Assert.Equal((elsewhere.Mrn, other), (record!.Patient.Mrn, record.Patient.FacilityId)); // still where the person was registered
    }

    /// <summary>MDV-P1-072 mutation check: scope the duplicate query to the facility again and this fails.</summary>
    [Fact]
    public async Task ADuplicateAtAnotherLocationIsWarnedAbout()
    {
        var other = Guid.NewGuid();
        var dob = new DateOnly(1975, 6, 15);
        var first = await Registry(tenantA).RegisterAsync(IndiaFull(facility) with { FullName = "Imran Qureshi (synthetic)", DateOfBirth = dob, Identifiers = [] });

        var found = await Registry(tenantA, at: other).FindDuplicatesAsync(new("IN", "Imran Qureshi (synthetic)", dob, []));
        Assert.Equal(first.Mrn[^4..], Assert.Single(found).MrnTail);
        var ex = await Assert.ThrowsAsync<PatientValidationException>(() =>
            Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { FullName = "Imran Qureshi (synthetic)", DateOfBirth = dob, Identifiers = [] }));
        Assert.Contains(ex.Errors, e => e.Field == "duplicates");
    }

    /// <summary>MDV-P1-072: desks at two locations registering at once never get the same MRN (the counter row lock serialises them).</summary>
    [Fact]
    public async Task ConcurrentRegistrationsAtTwoLocationsGetDistinctMrns()
    {
        var other = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
        {
            var at = i % 2 == 0 ? facility : other;
            return Task.Run(() => Registry(tenantA, at: at).RegisterAsync(IndiaFull(at) with { Identifiers = [], DateOfBirth = new DateOnly(1950, 1, 1).AddDays(i * 400), FullName = $"Racer {(char)('A' + i)} Person (synthetic)" }));
        }));

        Assert.Equal(12, results.Select(r => r.Mrn).Distinct().Count());
    }

    /// <summary>Completing a record is organisation-wide too; a command naming another facility than the active one is still refused before anything is written.</summary>
    [Fact]
    public async Task ARecordFromAnotherLocationCanBeCompletedButRegistrationMustBeAtTheActiveFacility()
    {
        var other = Guid.NewGuid();
        var elsewhere = await Registry(tenantA, at: other).RegisterAsync(UaeQuick(other));

        var address = new PatientAddress { Line1 = "Villa 1", Line2 = "Al Reem", Region = "AUH" };
        Assert.Equal(RegistrationStatus.Incomplete, await Registry(tenantA).CompleteRegistrationAsync(elsewhere.PatientId, address, new(PayerKind.SelfPay)));
        await using (var audit = sql.Audit(tenantA))
            Assert.True(await audit.Events.AnyAsync(e => e.PatientId == elsewhere.PatientId && e.Action == "patient.update"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Registry(tenantA).RegisterAsync(UaeQuick(other)));
        await using var check = sql.Patients(tenantA);
        Assert.Equal(1, await check.Patients.CountAsync(p => p.FacilityId == other));
    }

    /// <summary>MDV-AUDIT-1101: the desk corrects the phone and email; a bad number saves nothing; another tenant's patient is not found and stays as it was.</summary>
    [Fact]
    public async Task AContactCorrectionIsValidatedAsAtRegistrationAndStopsAtTheTenant()
    {
        var quick = await Registry(tenantA).RegisterAsync(UaeQuick(facility));

        await Assert.ThrowsAsync<PatientValidationException>(() => Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, null, null, contact: new("12", null, "no-at-sign")));
        Assert.Null(await Registry(tenantB).CompleteRegistrationAsync(quick.PatientId, null, null, contact: new("+971 50 123 4567", null, null)));

        Assert.NotNull(await Registry(tenantA).CompleteRegistrationAsync(quick.PatientId, null, null, contact: new("+971 50 123 4567", null, "synthetic@example.test")));
        await using var db = sql.Patients(tenantA);
        var row = await db.Patients.SingleAsync(p => p.Id == quick.PatientId);
        Assert.Equal(("+971501234567", "synthetic@example.test"), (row.Mobile, row.Email));
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
        var registered = await Registry(tenantA, at: other).RegisterAsync(UaeQuick(other));

        // What a DBA without the data key gets (MDV-P0-035): ciphertext and an HMAC, never the Emirates ID in any encoding.
        await using var conn = await sql.OpenAs("dbo", tenantA);
        await using var cmd = new NpgsqlCommand("""SELECT "LookupValue", "LookupIndex" FROM patients."Identifiers" WHERE "Scheme" = 'EmiratesId' AND "PatientId" = @p""", conn);
        cmd.Parameters.AddWithValue("p", registered.PatientId);
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            foreach (var stored in new[] { Assert.IsType<byte[]>(r[0]), Assert.IsType<byte[]>(r[1]) })
                foreach (var plain in new[] { "784199012345671", "784-1990-1234567-1" })
                {
                    Assert.DoesNotContain(plain, System.Text.Encoding.Latin1.GetString(stored), StringComparison.Ordinal);
                    Assert.DoesNotContain(plain, Convert.ToHexString(stored), StringComparison.Ordinal);
                }
        }

        // The application, with the key, reads the value back.
        await using var db = sql.Patients(tenantA);
        Assert.Equal("784-1990-1234567-1", (await db.Identifiers.SingleAsync(i => i.PatientId == registered.PatientId && i.Scheme == IdentifierScheme.EmiratesId)).LookupValue);

        // The protected-column list (data-classification.md), kept deliberately small: ciphertext, the blind index and the wrapped data keys
        // (the hash-chain columns of ConsentReceipts are integrity hashes, MDV-P0-036, not protected values).
        await using var columns = new NpgsqlCommand("""
            SELECT string_agg(table_name || '.' || column_name, ',' ORDER BY table_name, column_name)
            FROM information_schema.columns WHERE table_schema = 'patients' AND data_type = 'bytea' AND column_name NOT IN ('PrevHash', 'RowHash')
            """, conn);
        Assert.Equal("DataKeys.Wrapped,Identifiers.DisplayValue,Identifiers.LookupIndex,Identifiers.LookupValue,PayerCoverages.MemberNumber", await columns.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ALikeSearchOnAnEncryptedColumnIsRejected()
    {
        var other = Guid.NewGuid();
        await Registry(tenantA, at: other).RegisterAsync(UaeQuick(other));

        // Only the blind index can match (equality). EF refuses a pattern search on the value, and SQL sees ciphertext, so a prefix finds nothing.
        await using var db = sql.Patients(tenantA);
        var prefix = "784%";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Identifiers.Where(i => EF.Functions.Like(i.LookupValue, prefix)).ToListAsync());
        await using var conn = await sql.OpenAs("app_user", tenantA);
        // Anywhere in the stored bytes, not just at the start (random ciphertext holds a given 3-byte run with odds of about 1 in 400,000).
        await using var cmd = new NpgsqlCommand("""SELECT count(*) FROM patients."Identifiers" WHERE encode("LookupValue", 'escape') LIKE '%' || @prefix""", conn);
        cmd.Parameters.AddWithValue("prefix", prefix);
        Assert.Equal(0L, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ReportingSeesMaskedDemographicsWhileTheAppSeesClearValues()
    {
        var other = Guid.NewGuid();
        var registered = await Registry(tenantA, at: other).RegisterAsync(IndiaFull(other) with { DateOfBirth = new DateOnly(1980, 1, 1) });

        async Task<Dictionary<string, object>> Row(string user, string query)
        {
            await using var conn = await sql.OpenAs(user, tenantA);
            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.Parameters.AddWithValue("id", registered.PatientId);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, reader.GetValue);
        }

        // MDV-P0-037: the masked view gives initials and an age band, and has no name, date of birth, contact or address column at all.
        var masked = await Row("reporting_user", """SELECT * FROM reporting."Patients" WHERE "Id" = @id""");
        Assert.Equal(("TP", $"{(DateTime.UtcNow.Year - 1980) / 10 * 10}-{(DateTime.UtcNow.Year - 1980) / 10 * 10 + 9}"), ((string)masked["Initials"], (string)masked["AgeBand"]));
        Assert.DoesNotContain(masked.Keys, k => k is "FullName" or "ArabicName" or "DateOfBirth" or "Mobile" or "AlternatePhone" or "Email" or "GenderIdentity" || k.StartsWith("Address", StringComparison.Ordinal));
        Assert.DoesNotContain(masked.Values.OfType<string>(), v => v.Contains("Test Patient", StringComparison.Ordinal) || v.Contains("9000000002", StringComparison.Ordinal) || v.Contains('@'));

        // An identifier shows its scheme and length only (the ABHA is stored as its 14 digits).
        await using (var conn = await sql.OpenAs("reporting_user", tenantA))
        {
            await using var ids = new NpgsqlCommand("""SELECT * FROM reporting."Identifiers" WHERE "PatientId" = @id AND "Scheme" = 'Abha'""", conn);
            ids.Parameters.AddWithValue("id", registered.PatientId);
            await using var r = await ids.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(["Id", "TenantId", "PatientId", "Scheme", "ValueLength"], Enumerable.Range(0, r.FieldCount).Select(r.GetName));
            Assert.Equal(14, r.GetInt32(4));
        }

        // The base tables are denied to reporting outright, and another tenant's session sees nothing in the views.
        foreach (var table in new[] { "Patients", "Identifiers", "PayerCoverages", "Relationships", "DataKeys" })
        {
            await using var conn = await sql.OpenAs("reporting_user", tenantA);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand($"""SELECT 1 FROM patients."{table}" LIMIT 1""", conn).ExecuteScalarAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }
        await using (var conn = await sql.OpenAs("reporting_user", tenantB))
        await using (var count = new NpgsqlCommand("""SELECT count(*) FROM reporting."Patients" WHERE "Id" = @id""", conn))
        {
            count.Parameters.AddWithValue("id", registered.PatientId);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }

        var clear = await Row("app_user", "SELECT \"FullName\", \"Mobile\", \"Email\", \"DateOfBirth\", \"Address_PostalCode\" FROM patients.\"Patients\" WHERE \"Id\" = @id");
        Assert.Equal(["Test Patient (synthetic)", "+919000000002", "patient@example.test", new DateOnly(1980, 1, 1), "682001"], clear.Values);
    }

    sealed class FixedUser(Guid id) : ICurrentUser { public Guid? UserId => id; }

    internal sealed class FixedFacility(Guid? id) : ICurrentFacility { public Guid? FacilityId => id; }

    /// <summary>Every facility in this IANA time zone (the registration day is taken there, MDV-P1-020).</summary>
    internal sealed class FacilityZone(string zone) : Medvya.Tenancy.Application.IFacilityDirectory
    {
        public Task<Medvya.Tenancy.Application.FacilityInfo?> FindAsync(Guid facilityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Medvya.Tenancy.Application.FacilityInfo?>(new(facilityId, "Synthetic facility", zone, "Synthetic clinic"));
        public Task<Medvya.Tenancy.Domain.Role?> ActiveRoleAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ActiveDoctorsAsync(Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Medvya.Tenancy.Application.PrescriberInfo?> PrescriberAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, Medvya.Tenancy.Application.PrescriberInfo>> PrescribersAsync(Guid facilityId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<DateOnly, string>> ClosuresAsync(Guid facilityId, DateOnly first, DateOnly last, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Medvya.Tenancy.Application.PrintImages> PrintImagesAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    sealed class ThrowingLog : IAuditLog
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) => throw new InvalidOperationException("audit store down");
        public Task WriteAsync(AuditEvent auditEvent, System.Data.Common.DbTransaction transaction, CancellationToken ct) => throw new InvalidOperationException("audit store down");
    }
}
