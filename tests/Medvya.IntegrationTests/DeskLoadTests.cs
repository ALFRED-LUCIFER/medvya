using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit.Abstractions;
using static Medvya.IntegrationTests.SchedulingHarness;

namespace Medvya.IntegrationTests;

/// <summary>Runs only when MEDVYA_LOAD=1 (a load run takes minutes and loads Docker); `pnpm load:desk` sets it.</summary>
public sealed class LoadFactAttribute : FactAttribute
{
    public LoadFactAttribute() { if (Environment.GetEnvironmentVariable("MEDVYA_LOAD") != "1") Skip = "set MEDVYA_LOAD=1 to run the desk load test"; }
}

/// <summary>One step of the desk flow: its timings and failures, the budget it must meet (p95, ms).</summary>
internal sealed class StepStats(string name, int budgetMs)
{
    readonly List<long> timings = [];
    public string Name { get; } = name;
    public int BudgetMs { get; } = budgetMs;
    public int Errors { get; private set; }
    public int Count => timings.Count + Errors;
    public void Add(long ms, bool ok) { lock (timings) { if (ok) timings.Add(ms); else Errors++; } }

    /// <summary>Nearest-rank percentile of the successful calls.</summary>
    public long Percentile(double p)
    {
        lock (timings)
        {
            if (timings.Count == 0) return 0;
            var sorted = timings.Order().ToList();
            return sorted[(int)Math.Ceiling(sorted.Count * p) - 1];
        }
    }

    /// <summary>The first failure: status code and step only, never a body.</summary>
    public string? FirstError { get; set; }

    public bool WithinBudget => Count > 0 && Errors == 0 && Percentile(0.95) < BudgetMs;
    public string Row => string.Create(CultureInfo.InvariantCulture,
        $"| {Name} | {Count} | {Percentile(0.5)} | {Percentile(0.95)} | {Percentile(0.99)} | {(Count == 0 ? 0 : 100.0 * Errors / Count):0.0} % | < {BudgetMs} | {(WithinBudget ? "pass" : "FAIL")}{(FirstError is null ? "" : $" (first error: {FirstError})")} |");
}

/// <summary>
/// MDV-P1-037, the local part: the desk flow under the P1 load profile (20 receptionists), over HTTP against a running API.
/// Local mode seeds a busy clinic on Testcontainers PostgreSQL and starts the host on a loopback port; set MEDVYA_LOAD_URL to point
/// the same flow at another environment (staging), with MEDVYA_LOAD_DESKS (JSON array of request-header objects, one per desk, the
/// credentials), MEDVYA_LOAD_FACILITY, MEDVYA_LOAD_DOCTORS (comma-separated calendar ids), MEDVYA_LOAD_BOOK_FROM (a Monday, yyyy-MM-dd,
/// from which every doctor has free slots). Other knobs: MEDVYA_LOAD_SECONDS (180), MEDVYA_LOAD_DESKS_COUNT (20), MEDVYA_LOAD_THINK_MS (30000: 20 desks then book about 2,400 an hour, 12 times the pilot's 200),
/// MEDVYA_LOAD_REPORT (report path). Never run it against production.
/// </summary>
[Collection(nameof(RunsAlone))]
public sealed class DeskLoadTests(ITestOutputHelper output)
{
    const int PatientCount = 20_000;
    static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    static readonly string[] Times = [.. Enumerable.Range(0, 9).Select(i => $"{9 + i / 3:D2}:{i % 3 * 20:D2}"), .. Enumerable.Range(0, 9).Select(i => $"{17 + i / 3:D2}:{i % 3 * 20:D2}")];

    static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    [Fact]
    public void PercentileIsNearestRankAndABudgetNeedsNoErrors()
    {
        var s = new StepStats("x", 100);
        for (var i = 1; i <= 100; i++) s.Add(i, true);
        Assert.Equal(50, s.Percentile(0.5));
        Assert.Equal(95, s.Percentile(0.95));
        Assert.Equal(99, s.Percentile(0.99));
        Assert.True(s.WithinBudget);
        s.Add(0, false);
        Assert.False(s.WithinBudget); // one failed call fails the run
    }

    [LoadFact]
    public async Task DesksStayInsideTheLatencyBudgets()
    {
        var seconds = int.Parse(Env("MEDVYA_LOAD_SECONDS", "180"), CultureInfo.InvariantCulture);
        var desks = int.Parse(Env("MEDVYA_LOAD_DESKS_COUNT", "20"), CultureInfo.InvariantCulture);
        var think = TimeSpan.FromMilliseconds(int.Parse(Env("MEDVYA_LOAD_THINK_MS", "30000"), CultureInfo.InvariantCulture));

        PostgresFixture? sql = null;
        WebApplicationFactory<Program>? host = null;
        try
        {
            Uri url;
            List<Dictionary<string, string>> deskHeaders;
            Guid facility;
            Guid[] doctors;
            DateOnly bookFrom;
            string target;
            if (Environment.GetEnvironmentVariable("MEDVYA_LOAD_URL") is { Length: > 0 } external)
            {
                (url, target) = (new Uri(external), external);
                deskHeaders = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(Env("MEDVYA_LOAD_DESKS", "[]"))!;
                facility = Guid.Parse(Env("MEDVYA_LOAD_FACILITY", ""));
                doctors = [.. Env("MEDVYA_LOAD_DOCTORS", "").Split(',').Select(Guid.Parse)];
                bookFrom = DateOnly.Parse(Env("MEDVYA_LOAD_BOOK_FROM", ""), CultureInfo.InvariantCulture);
                Assert.NotEmpty(deskHeaders);
            }
            else
            {
                sql = new PostgresFixture();
                await sql.InitializeAsync();
                var h = new SchedulingHarness(sql);
                await h.SeedAsync();
                var third = Guid.NewGuid();
                await using (var db = sql.Context(h.Tenant))
                {
                    db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = third, FacilityId = h.Facility, Role = Role.SoloDoctor, Status = MembershipStatus.Active });
                    await db.SaveChangesAsync();
                }
                await using (var identity = sql.Identity())
                {
                    identity.Users.Add(new StaffUser { Id = third, Issuer = "test-idp", Subject = third.ToString(), DisplayName = "Dr. Fatima Haddad (synthetic)" });
                    await identity.SaveChangesAsync();
                }
                doctors = [h.Doctor, h.SecondDoctor, third];
                foreach (var d in doctors) await h.Schedule(h.Admin).SaveTemplateAsync(h.DoctorTemplate() with { CalendarId = d });
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist).DateTime);
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                // The data volume of a busy year for two clinics: a fully booked year for 3 doctors at each facility and 20,000 patients.
                var patients = await ScheduleReadPerformanceTests.SeedPatientsAsync(sql, h.Tenant, h.Facility, PatientCount);
                await ScheduleReadPerformanceTests.SeedVisitsAsync(sql, h.Tenant, h.Facility, doctors, patients, monday);
                await ScheduleReadPerformanceTests.SeedVisitsAsync(sql, h.Tenant, h.OtherFacility, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], patients, monday);
                var receptionists = await h.AddReceptionistsAsync(desks);
                // The rate-limit settings are raised here only: the load comes from 20 desks behind one address, which production limits would refuse.
                host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
                    .UseSetting("ConnectionStrings:Medvya", h.ConnectionString)
                    .UseSetting("RateLimiting:PermitsPerMinute", "10000000")
                    .UseSetting("RateLimiting:PatientSearchPerMinute", "10000000")
                    .UseSetting("RateLimiting:PatientRecordsPerMinute", "10000000")
                    .ConfigureTestServices(s => PatientEndpointTests.SignInFromHeaders(s, h.Tenant)));
                host.UseKestrel(0); // a real socket, so the numbers include HTTP and not only the pipeline
                host.StartServer();
                url = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
                target = $"local: Docker PostgreSQL 16 + Kestrel on loopback ({url})";
                deskHeaders = [.. receptionists.Select(r => new Dictionary<string, string> { ["X-Test-User"] = r.ToString(), ["X-Test-Facility"] = h.Facility.ToString() })];
                (facility, bookFrom) = (h.Facility, monday.AddDays(7 * (ScheduleReadPerformanceTests.Weeks + 1)));
            }

            static StepStats[] NewSteps() =>
            [
                new("Patient search", 300), new("Confirm patient", 300), new("Hold slot", 300), new("Book", 300),
                new("Check in", 600), new("Board read", 300), new("Day schedule read", 300),
            ];

            using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 200 }) { BaseAddress = url, Timeout = TimeSpan.FromSeconds(30) };
            var hotDay = Env("MEDVYA_LOAD_HOT_DAY", "") == "1"; // every desk books the same doctor-day, the worst case for its lock
            var slot = -1;
            var flows = 0;

            async Task<JsonElement?> Call(StepStats step, Dictionary<string, string> headers, HttpMethod method, string path, object? body = null, string? key = null)
            {
                using var req = new HttpRequestMessage(method, path);
                foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
                if (key is not null) req.Headers.Add("Idempotency-Key", key);
                if (body is not null) req.Content = JsonContent.Create(body);
                var clock = Stopwatch.StartNew();
                try
                {
                    using var res = await http.SendAsync(req);
                    var text = await res.Content.ReadAsStringAsync();
                    step.Add(clock.ElapsedMilliseconds, res.IsSuccessStatusCode);
                    if (!res.IsSuccessStatusCode) step.FirstError ??= $"{(int)res.StatusCode} {res.StatusCode}{(res.StatusCode == System.Net.HttpStatusCode.Conflict ? " " + text[..Math.Min(text.Length, 160)] : "")}"; // status, and the code of a booking conflict; never another body
                    return res.IsSuccessStatusCode && text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : null;
                }
                catch (HttpRequestException e) { step.FirstError ??= $"{e.GetType().Name} {e.Message} {e.InnerException?.Message}"; step.Add(clock.ElapsedMilliseconds, false); return null; }
                catch (TaskCanceledException) { step.FirstError ??= "timeout"; step.Add(clock.ElapsedMilliseconds, false); return null; }
            }

            async Task Desk(int n, StepStats[] steps, DateTimeOffset until)
            {
                var (search, confirm, hold, book, checkIn, board, day) = (steps[0], steps[1], steps[2], steps[3], steps[4], steps[5], steps[6]);
                var headers = deskHeaders[n % deskHeaders.Count];
                var rnd = new Random(n);
                // Desks start at random moments and pause 0.5 to 1.5 times the think time, so they do not move in lockstep (a real front desk does not).
                TimeSpan Pause() => think * (0.5 + rnd.NextDouble());
                await Task.Delay(think * rnd.NextDouble() / 2);
                while (DateTimeOffset.UtcNow < until)
                {
                    // Every flow takes its own free slot (weeks 27 on, weekdays, 18 slots a day per doctor), spread across doctors and days
                    // like a clinic's bookings, or all on one doctor-day when MEDVYA_LOAD_HOT_DAY=1.
                    var i = Interlocked.Increment(ref slot);
                    var (doctor, dayIndex, time) = hotDay ? (i / Times.Length % doctors.Length, i / (Times.Length * doctors.Length), i % Times.Length)
                        : (i % doctors.Length, i / doctors.Length % 70, i / (doctors.Length * 70) % Times.Length);
                    var date = bookFrom.AddDays(dayIndex / 5 % 14 * 7 + dayIndex % 5);
                    var start = new DateTimeOffset(DateTime.Parse($"{date:yyyy-MM-dd}T{Times[time]}:00", CultureInfo.InvariantCulture), TimeSpan.FromHours(5.5));

                    var found = await Call(search, headers, HttpMethod.Post, "/v1/patients/search", new { query = (100_000 + rnd.Next(PatientCount)).ToString(CultureInfo.InvariantCulture), filter = "All" });
                    if (found?.GetProperty("items").EnumerateArray().FirstOrDefault() is not { ValueKind: JsonValueKind.Object } p) { await Task.Delay(Pause()); continue; }
                    var patient = p.GetProperty("id").GetGuid();
                    await Call(confirm, headers, HttpMethod.Get, FormattableString.Invariant($"/v1/schedule/patients/{patient}?facilityId={facility}"));
                    var held = await Call(hold, headers, HttpMethod.Post, "/v1/slot-holds", new { facilityId = facility, calendarId = doctors[doctor], start, visitType = "FollowUp" });
                    if (held is null) { await Task.Delay(Pause()); continue; }
                    if (await Call(book, headers, HttpMethod.Post, "/v1/appointments",
                        new { holdId = held.Value.GetProperty("id").GetGuid(), patientId = patient, reasonCategory = "General", channel = "Phone" }, Guid.NewGuid().ToString()) is null) { await Task.Delay(Pause()); continue; }
                    // The board shows today: the desk checks in a patient who has arrived (a booked visit of today; a repeat is answered, not refused).
                    var cards = await Call(board, headers, HttpMethod.Get, FormattableString.Invariant($"/v1/front-desk/board?facilityId={facility}"));
                    var arrivals = cards?.GetProperty("cards").EnumerateArray().Where(c => c.GetProperty("state").GetString() == "Booked").ToList() ?? [];
                    if (arrivals.Count > 0)
                        await Call(checkIn, headers, HttpMethod.Post, FormattableString.Invariant($"/v1/appointments/{arrivals[i % arrivals.Count].GetProperty("id").GetGuid()}/check-in"), new { elapsedMs = 5000 });
                    await Call(day, headers, HttpMethod.Get, FormattableString.Invariant($"/v1/schedule?facilityId={facility}"));
                    Interlocked.Increment(ref flows);
                    await Task.Delay(Pause());
                }
            }

            await Task.WhenAll(Enumerable.Range(0, 3).Select(n => Desk(n, NewSteps(), DateTimeOffset.UtcNow.AddSeconds(5)))); // warm-up: plans, pools, JIT; not measured
            flows = 0;
            var steps = NewSteps();
            var wall = Stopwatch.StartNew();
            var stop = DateTimeOffset.UtcNow.AddSeconds(seconds);
            var waits = new Dictionary<string, int>();
            using var sampling = new CancellationTokenSource();
            var sampler = sql is null ? Task.CompletedTask : SampleWaitsAsync(sql.ConnectionString, waits, sampling.Token);
            await Task.WhenAll(Enumerable.Range(0, desks).Select(n => Desk(n, steps, stop)));
            var elapsed = wall.Elapsed;
            await sampling.CancelAsync();
            await sampler;
            var topWaits = string.Join("\n", waits.OrderByDescending(w => w.Value).Take(8).Select(w => $"  - {w.Key}: {w.Value} samples"));

            var table = new StringBuilder("| Step | Calls | p50 ms | p95 ms | p99 ms | Errors | p95 budget ms | Result |\n|---|---:|---:|---:|---:|---:|---:|---|\n");
            foreach (var s in steps) table.AppendLine(s.Row);
            var rate = flows / elapsed.TotalHours;
            var report = FormattableString.Invariant($"""
                # Desk flow load test (MDV-P1-037)

                Generated by `pnpm load:desk` (`tests/Medvya.IntegrationTests/DeskLoadTests.cs`). Synthetic data only. Do not edit by hand.

                - Target: {target}{(hotDay ? "; every desk on one doctor-day (MEDVYA_LOAD_HOT_DAY=1)" : "")}
                - Profile: {desks} concurrent receptionists, {think.TotalMilliseconds:0} ms think time between flows, {elapsed.TotalSeconds:0} s, {flows} complete flows ({rate:0} bookings/hour, the ticket asks for 200)
                - Flow per desk: search, confirm patient, hold, book, check in, board read, day schedule read
                - Data (local run): {PatientCount} patients; 3 doctors fully booked for a year at each of 2 facilities (about 32,000 visits)
                - Budgets: reads p95 < 300 ms, booking p95 < 300 ms, writes p95 < 600 ms, no errors

                {table}
                Where the database waited (sampled every 250 ms; wait type, then the start of the waiting statement; local run only):

                {(topWaits.Length == 0 ? "  - (not sampled)" : topWaits)}
                """);
            output.WriteLine(report);
            var path = Env("MEDVYA_LOAD_REPORT", Path.Combine(RepoRoot(), "docs", "05-delivery", "load-test-desk.md"));
            await File.WriteAllTextAsync(path, report);

            Assert.All(steps, s => Assert.True(s.WithinBudget, s.Row));
        }
        finally
        {
            host?.Dispose();
            if (sql is not null) await sql.DisposeAsync();
        }
    }

    /// <summary>Counts what waiting requests wait on, so a slow step points at a lock, the log or the CPU. Statement text carries parameter markers only.</summary>
    static async Task SampleWaitsAsync(string connectionString, Dictionary<string, int> waits, CancellationToken stop)
    {
        await using var conn = new Npgsql.NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);
        await using var cmd = new Npgsql.NpgsqlCommand("""
            SELECT wait_event_type || ':' || wait_event, left(regexp_replace(query, '\s+', ' ', 'g'), 90)
            FROM pg_stat_activity
            WHERE pid <> pg_backend_pid() AND state = 'active' AND wait_event IS NOT NULL AND wait_event_type NOT IN ('Activity', 'Client')
            """, conn);
        while (!stop.IsCancellationRequested)
        {
            await using (var rows = await cmd.ExecuteReaderAsync(CancellationToken.None))
                while (await rows.ReadAsync(CancellationToken.None))
                {
                    var key = $"{rows.GetString(0)} on `{rows.GetString(1).Trim().Replace("|", "/", StringComparison.Ordinal)}`";
                    waits[key] = waits.GetValueOrDefault(key) + 1;
                }
            try { await Task.Delay(250, stop); } catch (OperationCanceledException) { }
        }
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Medvya.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Medvya.sln not found above the test binaries");
    }
}
