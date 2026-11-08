using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Medvya.Patients.Application;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Medvya.IntegrationTests;

/// <summary>Timing tests run alone: other test classes sharing the CPU would measure the runner, not the query.</summary>
[CollectionDefinition(nameof(RunsAlone), DisableParallelization = true)]
public sealed class RunsAlone;

/// <summary>
/// MDV-P1-018 and MDV-P1-016 NFRs: desk search and the duplicate check, p95 under 300 ms with 40,000 synthetic patients in one
/// facility (real PostgreSQL), name typos included. Measured through the endpoints, so the capability check, the audit write and the Always Encrypted
/// identifier lookup are all inside the timing.
/// </summary>
[Collection(nameof(RunsAlone))]
public sealed class PatientSearchPerformanceTests(PostgresFixture sql, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    // MEDVYA_SEARCH_PATIENTS=200000 re-measures at a larger practice (the budget still applies); the default is the pilot target.
    static readonly int Patients = int.TryParse(Environment.GetEnvironmentVariable("MEDVYA_SEARCH_PATIENTS"), CultureInfo.InvariantCulture, out var n) ? n : 40_000;
    const int MrnStart = 100_000;
    readonly Guid receptionist = Guid.NewGuid();
    Guid tenant, facility;
    Guid[] ids = [];

    /// <summary>ABHA number of synthetic patient n: "91" and n in 12 digits (14 digits, as ABHA; not a real ABHA).</summary>
    static string Abha(int n) => "91" + n.ToString("D12", CultureInfo.InvariantCulture);

    /// <summary>
    /// The same 40,000-patient synthetic practice the load tests and the Azure run use (tools/synthetic-data-generator), plus one ABHA each,
    /// encrypted and blind-indexed with the fixture's keys exactly as the API stores them (MDV-P0-035).
    /// </summary>
    async Task SeedAsync()
    {
        (tenant, facility, ids, _) = await Medvya.SyntheticData.SyntheticPatients.SeedAsync(sql.ConnectionString, "Development", "Search Perf Clinic", Patients, MrnStart, memberUserId: receptionist);
        var identifiers = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("Id", typeof(Guid)), ("TenantId", typeof(Guid)), ("PatientId", typeof(Guid)), ("Scheme", typeof(string)), ("LookupValue", typeof(byte[])), ("LookupIndex", typeof(byte[])),
        }) identifiers.Columns.Add(name, type);
        for (var i = 1; i <= Patients; i++)
        {
            var identifierId = Guid.CreateVersion7();
            identifiers.Rows.Add(identifierId, tenant, ids[i - 1], "Abha", sql.NationalIds.Encrypt(tenant, NationalIdColumns.IdentifierLookup, identifierId, Abha(i)), sql.NationalIds.Index(tenant, Abha(i)));
        }
        await using var conn = await sql.OpenAs("dbo", tenant);
        await PostgresFixture.BulkCopyAsync(conn, "patients.Identifiers", identifiers);
    }

    [Fact]
    public async Task SearchAndDuplicateCheckP95StayUnder300MillisecondsWith40000Patients()
    {
        await SeedAsync();
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:PatientSearchPerMinute", "100000")
            .UseSetting("RateLimiting:DuplicateChecksPerMinute", "100000")
            .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, tenant)));
        using var desk = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        desk.DefaultRequestHeaders.Add("X-Test-User", receptionist.ToString());
        desk.DefaultRequestHeaders.Add("X-Test-Facility", facility.ToString());

        async Task<(long Ms, JsonElement Body)> Post(string path, object body)
        {
            var clock = Stopwatch.StartNew();
            using var res = await desk.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);
            var elapsed = clock.ElapsedMilliseconds;
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return (elapsed, await res.Content.ReadFromJsonAsync<JsonElement>());
        }

        // Name prefix, a spelling variant, a transposed and a missing letter, a partial two-word name, phone, MRN, exact ABHA, and the directory with its filters.
        string?[] queries = ["kavit", "mohamed", "krsihnan", "venkatsh", "sreen kumar", "+91 90 0011 2345", "112345", "91 0000 0000 0099", null];
        var filters = new[] { "All", "Incomplete", "Recent" };
        async Task<long> Search(string? query, string filter)
        {
            var (ms, body) = await Post("/v1/patients/search", new { query, filter });
            Assert.True(body.GetProperty("items").GetArrayLength() > 0, $"no rows for {query ?? "(directory)"} {filter}");
            return ms;
        }
        foreach (var q in queries) await Search(q, "All"); // warm the plan cache, the connection pool and the encryption metadata
        var searches = new List<long>();
        for (var round = 0; round < 8; round++)
            foreach (var q in queries)
                foreach (var f in filters.Take(q is null ? 3 : 1))
                    searches.Add(await Search(q, f));
        AssertP95(searches, "searches through the endpoint");

        // The generated patient 12345 gives a name and birthday that exist; the ABHA of patient 99 exists too, so both the name + DOB and the exact-identifier paths run.
        var known = Medvya.SyntheticData.SyntheticPatients.Generate(tenant, MrnStart, 12_344, DateTimeOffset.UtcNow);
        async Task<long> Check()
        {
            var (ms, body) = await Post("/v1/patients/duplicate-check",
                new { fullName = known.FullName, dateOfBirth = known.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), identifiers = new[] { new { scheme = "Abha", value = Abha(99) } } });
            Assert.True(body.GetProperty("candidates").GetArrayLength() > 0, "no duplicate candidates");
            return ms;
        }
        await Check();
        var checks = new List<long>();
        for (var i = 0; i < 30; i++) checks.Add(await Check());
        AssertP95(checks, "duplicate checks through the endpoint");
    }

    void AssertP95(List<long> timings, string what)
    {
        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(timings.Count * 0.95) - 1];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {Patients} patients, p50 {timings[timings.Count / 2]} ms, p95 {p95} ms, max {timings[^1]} ms over {timings.Count}"));
        Assert.True(p95 < 300, string.Create(CultureInfo.InvariantCulture, $"p95 {p95} ms over {timings.Count} {what} (max {timings[^1]} ms)"));
    }
}
