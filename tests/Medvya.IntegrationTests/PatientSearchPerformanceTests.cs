using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Medvya.Patients.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>Timing tests run alone: other test classes sharing the CPU would measure the runner, not the query.</summary>
[CollectionDefinition(nameof(RunsAlone), DisableParallelization = true)]
public sealed class RunsAlone;

/// <summary>
/// MDV-P1-018 NFR: desk search p95 under 300 ms with 50,000 synthetic patients in one facility (real SQL Server), measured through
/// the endpoint, so the capability check, the audit write and the Always Encrypted identifier lookup are all inside the timing.
/// </summary>
[Collection(nameof(RunsAlone))]
public sealed class PatientSearchPerformanceTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    const int Patients = 50_000;
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid(), receptionist = Guid.NewGuid();

    static readonly string[] Given = ["Meera", "Deepak", "Fatima", "Arjun", "Kavya", "Rahul", "Priya", "Omar", "Lakshmi", "Suresh", "Anita", "Ravi"];
    static readonly string[] Family = ["Iyer", "Nair", "Menon", "Rao", "Pillai", "Khan", "Haddad", "Thomas", "Joseph", "Varghese", "Reddy", "Shah"];

    /// <summary>ABHA number of synthetic patient n: "91" and n in 12 digits (14 digits, as ABHA; not a real ABHA).</summary>
    static string Abha(int n) => "91" + n.ToString("D12", CultureInfo.InvariantCulture);

    /// <summary>
    /// Bulk-loads synthetic patients and one ABHA each inside the tenant (row-level security block predicates apply as for the API).
    /// The identifiers go through an Always Encrypted connection, so SqlClient encrypts them as the API would.
    /// </summary>
    async Task SeedAsync()
    {
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Perf Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Perf (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = receptionist, FacilityId = facility, Role = Role.Receptionist, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }

        var patients = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("FacilityId", typeof(Guid)), ("Mrn", typeof(string)), ("CountryCode", typeof(string)),
            ("FullName", typeof(string)), ("DateOfBirth", typeof(DateTime)), ("DateOfBirthEstimated", typeof(bool)), ("SexAtBirth", typeof(string)),
            ("Mobile", typeof(string)), ("Status", typeof(string)), ("RegisteredAt", typeof(DateTimeOffset)), ("ChangedAt", typeof(DateTimeOffset)),
        }) patients.Columns.Add(name, type);
        var identifiers = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("PatientId", typeof(Guid)), ("Scheme", typeof(string)), ("LookupValue", typeof(string)),
        }) identifiers.Columns.Add(name, type);

        var start = DateTimeOffset.UtcNow.AddMinutes(-Patients); // about one registration a minute, the last week "recent"
        for (var n = 1; n <= Patients; n++)
        {
            var id = Guid.CreateVersion7();
            patients.Rows.Add(id, tenant, facility, Mrn.Format(n), "IN",
                $"{Given[n % Given.Length]} {Family[n / Given.Length % Family.Length]} {n} (synthetic)", new DateTime(1940 + n % 80, 1 + n % 12, 1 + n % 28),
                false, n % 2 == 0 ? "Female" : "Male", $"+9190{n:D8}", n % 10 == 0 ? "Incomplete" : "Complete", start.AddMinutes(n), start.AddMinutes(n));
            identifiers.Rows.Add(Guid.CreateVersion7(), tenant, id, "Abha", Abha(n));
        }

        await using (var conn = await sql.OpenAs("dbo", tenant))
        {
            using var copy = new SqlBulkCopy(conn) { DestinationTableName = "patients.Patients", BulkCopyTimeout = 600 };
            foreach (DataColumn c in patients.Columns) copy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await copy.WriteToServerAsync(patients);
            await using var stats = new SqlCommand("UPDATE STATISTICS patients.Patients", conn);
            await stats.ExecuteNonQueryAsync();
        }
        await using (var encrypted = await sql.OpenAs("dbo", tenant, columnEncryption: true))
        {
            using var copy = new SqlBulkCopy(encrypted) { DestinationTableName = "patients.Identifiers", BulkCopyTimeout = 600 };
            foreach (DataColumn c in identifiers.Columns) copy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await copy.WriteToServerAsync(identifiers);
            await using var stats = new SqlCommand("UPDATE STATISTICS patients.Identifiers", encrypted);
            await stats.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SearchP95StaysUnder300MillisecondsWith50000Patients()
    {
        await SeedAsync();
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:PatientSearchPerMinute", "100000")
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new PatientEndpointTests.SignedIn(tenant))));
        using var desk = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        desk.DefaultRequestHeaders.Add("X-Test-User", receptionist.ToString());
        desk.DefaultRequestHeaders.Add("X-Test-Facility", facility.ToString());

        // Name, phone, MRN, exact ABHA (deterministic Always Encrypted), and the directory with its filters.
        string?[] queries = ["kavya", "deepak rao", "+91 90 0001 2345", "012345", "91 0000 0001 2345", null];
        var filters = new[] { "All", "Incomplete", "Recent" };

        async Task<long> Timed(string? query, string filter)
        {
            var clock = Stopwatch.StartNew();
            using var res = await desk.PostAsJsonAsync(new Uri("/v1/patients/search", UriKind.Relative), new { query, filter });
            var elapsed = clock.ElapsedMilliseconds;
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(body.GetProperty("items").GetArrayLength() > 0, $"no rows for {query ?? "(directory)"} {filter}");
            return elapsed;
        }

        foreach (var q in queries) await Timed(q, "All"); // warm the plan cache, the connection pool and the encryption metadata
        var timings = new List<long>();
        for (var round = 0; round < 8; round++)
            foreach (var q in queries)
                foreach (var f in filters.Take(q is null ? 3 : 1))
                    timings.Add(await Timed(q, f));

        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
        Assert.True(p95 < 300, string.Create(CultureInfo.InvariantCulture, $"p95 {p95} ms over {timings.Count} searches through the endpoint (max {timings[^1]} ms)"));
    }
}
