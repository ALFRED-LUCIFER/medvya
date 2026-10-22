using System.Data;
using System.Diagnostics;
using System.Globalization;
using Medvya.Audit.Infrastructure;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Medvya.IntegrationTests;

/// <summary>Timing tests run alone: other test classes sharing the CPU would measure the runner, not the query.</summary>
[CollectionDefinition(nameof(RunsAlone), DisableParallelization = true)]
public sealed class RunsAlone;

/// <summary>MDV-P1-018 NFR: desk search p95 under 300 ms with 50,000 synthetic patients in one facility (real SQL Server).</summary>
[Collection(nameof(RunsAlone))]
public sealed class PatientSearchPerformanceTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    const int Patients = 50_000;
    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid();

    static readonly string[] Given = ["Meera", "Deepak", "Fatima", "Arjun", "Kavya", "Rahul", "Priya", "Omar", "Lakshmi", "Suresh", "Anita", "Ravi"];
    static readonly string[] Family = ["Iyer", "Nair", "Menon", "Rao", "Pillai", "Khan", "Haddad", "Thomas", "Joseph", "Varghese", "Reddy", "Shah"];

    /// <summary>Bulk-loads synthetic rows inside the tenant (row-level security block predicates apply as for the API).</summary>
    async Task SeedAsync()
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("FacilityId", typeof(Guid)), ("Mrn", typeof(string)), ("CountryCode", typeof(string)),
            ("FullName", typeof(string)), ("DateOfBirth", typeof(DateTime)), ("DateOfBirthEstimated", typeof(bool)), ("SexAtBirth", typeof(string)),
            ("Mobile", typeof(string)), ("Status", typeof(string)), ("RegisteredAt", typeof(DateTimeOffset)), ("ChangedAt", typeof(DateTimeOffset)),
        }) table.Columns.Add(name, type);
        var start = DateTimeOffset.UtcNow.AddMinutes(-Patients); // about one registration a minute, the last week "recent"
        for (var n = 1; n <= Patients; n++)
        {
            table.Rows.Add(Guid.CreateVersion7(), tenant, facility, Mrn.Format(n), "IN",
                $"{Given[n % Given.Length]} {Family[n / Given.Length % Family.Length]} {n} (synthetic)", new DateTime(1940 + n % 80, 1 + n % 12, 1 + n % 28),
                false, n % 2 == 0 ? "Female" : "Male", $"+9190{n:D8}", n % 10 == 0 ? "Incomplete" : "Complete", start.AddMinutes(n), start.AddMinutes(n));
        }

        await using var conn = await sql.OpenAs("dbo", tenant);
        using var copy = new SqlBulkCopy(conn) { DestinationTableName = "patients.Patients", BulkCopyTimeout = 600 };
        foreach (DataColumn c in table.Columns) copy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await copy.WriteToServerAsync(table);
        await using var stats = new SqlCommand("UPDATE STATISTICS patients.Patients", conn);
        await stats.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task SearchP95StaysUnder300MillisecondsWith50000Patients()
    {
        await SeedAsync();
        string?[] queries = ["kavya", "deepak rao", "+91 90 0001 2345", "012345", null];
        var filters = new[] { DirectoryFilter.All, DirectoryFilter.Incomplete, DirectoryFilter.Recent };

        async Task<long> Timed(string? query, DirectoryFilter filter)
        {
            var registry = new PatientRegistry(sql.Patients(tenant), new SqlServerFixture.FixedTenant(tenant), new PatientRegistryTests.FixedFacility(facility),
                new AuditLogTestUser(), new AuditLog(sql.Audit(tenant)), TimeProvider.System);
            var clock = Stopwatch.StartNew();
            var result = await registry.SearchAsync(new("IN", query, filter));
            Assert.True(result.Items.Count > 0, $"no rows for {query ?? "(directory)"} {filter}");
            return clock.ElapsedMilliseconds;
        }

        foreach (var q in queries) await Timed(q, DirectoryFilter.All); // warm the plan cache and the connection pool
        var timings = new List<long>();
        for (var round = 0; round < 8; round++)
            foreach (var q in queries)
                foreach (var f in filters.Take(q is null ? 3 : 1))
                    timings.Add(await Timed(q, f));

        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
        Assert.True(p95 < 300, string.Create(CultureInfo.InvariantCulture, $"p95 {p95} ms over {timings.Count} searches (max {timings[^1]} ms)"));
    }

    sealed class AuditLogTestUser : Medvya.BuildingBlocks.Domain.ICurrentUser { public Guid? UserId { get; } = Guid.NewGuid(); }
}
