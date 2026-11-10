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
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Medvya.Scheduling.Api;
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
/// from which every doctor has free slots), MEDVYA_LOAD_EXTERNAL_ACK (the target's exact host) and MEDVYA_LOAD_REPORT outside docs/. Other knobs: MEDVYA_LOAD_SECONDS (180), MEDVYA_LOAD_DESKS_COUNT (20), MEDVYA_LOAD_THINK_MS (30000: 20 desks then book about 2,400 an hour, 12 times the pilot's 200),
/// MEDVYA_LOAD_REPORT (report path), MEDVYA_LOAD_RESULTS (JSON results path), MEDVYA_LOAD_BASELINE (a results file: the run fails when a
/// step's p95 is more than 20 % and 25 ms over it, the nightly regression check). Never run it against production.
/// </summary>
[Collection(nameof(RunsAlone))]
public sealed class DeskLoadTests(ITestOutputHelper output)
{
    const int PatientCount = 20_000;
    static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    static readonly string[] Times = [.. Enumerable.Range(0, 9).Select(i => $"{9 + i / 3:D2}:{i % 3 * 20:D2}"), .. Enumerable.Range(0, 9).Select(i => $"{17 + i / 3:D2}:{i % 3 * 20:D2}")];

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

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
                var refused = ExternalTargetRefusal(url, Environment.GetEnvironmentVariable("MEDVYA_LOAD_EXTERNAL_ACK"), Env("MEDVYA_LOAD_REPORT", DefaultReport()), RepoRoot());
                Assert.True(refused is null, refused);
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
                // Weekends too, so the check-in step has visits booked today on any day the run happens.
                await ScheduleReadPerformanceTests.SeedVisitsAsync(sql, h.Tenant, h.Facility, doctors, patients, monday, weekends: true);
                await ScheduleReadPerformanceTests.SeedVisitsAsync(sql, h.Tenant, h.OtherFacility, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], patients, monday, weekends: true);
                var receptionists = await h.AddReceptionistsAsync(desks);
                // Real server-side sessions, as after sign-in and the facility picker: every call and hub invocation checks one, as in production.
                var sessions = new BoardHubTests.SignedInDesks();
                foreach (var r in receptionists) await sessions.SignInAsync(sql, h.Tenant, r, h.Facility);
                // The rate-limit settings are raised here only: the load comes from 20 desks behind one address, which production limits would refuse.
                host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
                    .UseSetting("ConnectionStrings:Medvya", h.ConnectionString)
                    .UseSetting("RateLimiting:PermitsPerMinute", "10000000")
                    .UseSetting("RateLimiting:PatientSearchPerMinute", "10000000")
                    .UseSetting("RateLimiting:PatientRecordsPerMinute", "10000000")
                    .UseSetting("RateLimiting:BoardWatchPerMinute", "10000000")
                    .UseSetting("Logging:LogLevel:Default", "Warning") // closer to production, where the host keeps no console logging
                    .ConfigureTestServices(s => s.AddSingleton<IStartupFilter>(sessions)));
                host.UseKestrel(0); // a real socket, so the numbers include HTTP and not only the pipeline
                host.StartServer();
                url = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
                target = $"local: Docker PostgreSQL 16 + Kestrel on loopback ({url})";
                deskHeaders = [.. receptionists.Select(r => new Dictionary<string, string> { ["X-Test-User"] = r.ToString() })];
                (facility, bookFrom) = (h.Facility, monday.AddDays(7 * (ScheduleReadPerformanceTests.Weeks + 1)));
            }

            static StepStats[] NewSteps() =>
            [
                new("Patient search", 300), new("Confirm patient", 300), new("Hold slot", 300), new("Book", 300),
                new("Check in", 600), new("Board read", 300), new("Day schedule read", 300),
                new("Board push", 2000), // a change reaching every subscribed board, from the change to receipt; MDV-P1-029 asks for 2 s
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
                var (search, confirm, hold, book, checkIn, board, day, push) = (steps[0], steps[1], steps[2], steps[3], steps[4], steps[5], steps[6], steps[7]);
                var headers = deskHeaders[n % deskHeaders.Count];
                var rnd = new Random(n);
                // Every desk keeps the live board open, as the reception screen does: subscribed for the run, timing each change it hears.
                await using var live = new HubConnectionBuilder()
                    .WithUrl(new Uri(url, BoardHub.Path), Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets, o => { foreach (var (k, v) in headers) o.Headers[k] = v; })
                    .Build();
                live.On<JsonElement>(nameof(BoardChanged), m => push.Add((long)(DateTimeOffset.UtcNow - m.GetProperty("changedAt").GetDateTimeOffset()).TotalMilliseconds, true));
                try
                {
                    await live.StartAsync();
                    await live.InvokeAsync(nameof(BoardHub.Watch), facility);
                }
                catch (Exception e) when (e is HttpRequestException or HubException or System.Net.WebSockets.WebSocketException)
                {
                    push.FirstError ??= $"subscribe: {e.GetType().Name}";
                    push.Add(0, false);
                }
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
                - Flow per desk: search, confirm patient, hold, book, check in, board read, day schedule read; every desk subscribed to the live board throughout
                - Data (local run): {PatientCount} patients; 3 doctors fully booked for a year at each of 2 facilities (about 32,000 visits)
                - Budgets: reads p95 < 300 ms, booking p95 < 300 ms, writes p95 < 600 ms, no errors

                {table}
                Where the database waited (sampled every 250 ms; wait type, then the start of the waiting statement; local run only):

                {(topWaits.Length == 0 ? "  - (not sampled)" : topWaits)}
                """);
            output.WriteLine(report);
            var path = Env("MEDVYA_LOAD_REPORT", DefaultReport());
            await File.WriteAllTextAsync(path, report);
            var results = steps.ToDictionary(s => s.Name, s => new StepResult(s.Count, s.Errors, s.Percentile(0.5), s.Percentile(0.95), s.Percentile(0.99)));
            if (Environment.GetEnvironmentVariable("MEDVYA_LOAD_RESULTS") is { Length: > 0 } resultsPath)
                await File.WriteAllTextAsync(resultsPath, JsonSerializer.Serialize(results, Indented));

            Assert.All(steps, s => Assert.True(s.WithinBudget, s.Row));
            if (Environment.GetEnvironmentVariable("MEDVYA_LOAD_BASELINE") is { Length: > 0 } baselinePath)
            {
                var regressions = Regressions(results, JsonSerializer.Deserialize<Dictionary<string, StepResult>>(await File.ReadAllTextAsync(baselinePath))!);
                Assert.True(regressions.Count == 0, "p95 regressed more than 20 % against " + baselinePath + ":\n" + string.Join("\n", regressions));
            }
        }
        finally
        {
            host?.Dispose();
            if (sql is not null) await sql.DisposeAsync();
        }
    }

    static string DefaultReport() => Path.Combine(RepoRoot(), "docs", "05-delivery", "load-test-desk.md");

    /// <summary>
    /// Why an external run (MEDVYA_LOAD_URL) must not start, or null: the target's exact host must be acknowledged in MEDVYA_LOAD_EXTERNAL_ACK,
    /// so a typo cannot load another site, and its report must be written outside the repo's docs/ folder (only local reports are committed).
    /// </summary>
    internal static string? ExternalTargetRefusal(Uri url, string? ack, string reportPath, string repoRoot)
    {
        if (!string.Equals(url.Host, ack, StringComparison.OrdinalIgnoreCase))
            return $"external load target {url.Host}: set MEDVYA_LOAD_EXTERNAL_ACK={url.Host} to confirm this exact host";
        var docs = Path.GetFullPath(Path.Combine(repoRoot, "docs")) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(reportPath).StartsWith(docs, StringComparison.OrdinalIgnoreCase)
            ? "an external run writes its report outside docs/: set MEDVYA_LOAD_REPORT (for example /tmp/azure.md)"
            : null;
    }

    [Fact]
    public void AnExternalRunNeedsItsExactHostAcknowledgedAndAReportOutsideDocs()
    {
        var url = new Uri("https://app-medvya-dev.example.test/");
        var repo = Path.Combine(Path.GetTempPath(), "repo");
        var outside = Path.Combine(Path.GetTempPath(), "azure.md");
        Assert.NotNull(ExternalTargetRefusal(url, null, outside, repo));
        Assert.NotNull(ExternalTargetRefusal(url, "app-medvya-dev.example.tes", outside, repo)); // a typo is not the host
        Assert.NotNull(ExternalTargetRefusal(url, "app-medvya-dev.example.test", Path.Combine(repo, "docs", "05-delivery", "load-test-desk.md"), repo));
        Assert.NotNull(ExternalTargetRefusal(url, "app-medvya-dev.example.test", Path.Combine(repo, "tests", "..", "docs", "x.md"), repo));
        Assert.Null(ExternalTargetRefusal(url, "app-medvya-dev.example.test", outside, repo));
        Assert.Null(ExternalTargetRefusal(url, "app-medvya-dev.example.test", Path.Combine(repo, "docs-out", "x.md"), repo));
    }

    /// <summary>One step's numbers in the results and baseline files.</summary>
    internal sealed record StepResult(int Calls, int Errors, long P50, long P95, long P99);

    /// <summary>
    /// Steps whose p95 is more than 20 % and at least <see cref="NoiseFloorMs"/> over the baseline's (MDV-P1-037 nightly check).
    /// A step the baseline lacks is new and has nothing to regress from.
    /// </summary>
    internal static List<string> Regressions(Dictionary<string, StepResult> now, Dictionary<string, StepResult> baseline) =>
        [.. now.Where(s => baseline.TryGetValue(s.Key, out var b) && s.Value.P95 * 5 > b.P95 * 6 && s.Value.P95 - b.P95 >= NoiseFloorMs)
            .Select(s => FormattableString.Invariant($"{s.Key}: p95 {s.Value.P95} ms, baseline {baseline[s.Key].P95} ms (+{100.0 * (s.Value.P95 - baseline[s.Key].P95) / Math.Max(1, baseline[s.Key].P95):0} %)"))];

    /// <summary>
    /// Two CI runs of the same commit differed by 14 ms (+33 %) on search p95 (42 and 56 ms): runners vary, and most steps take 5 to 20 ms.
    /// shortcut: a fixed floor stands in for runner noise; drop it when the nightly runs on a dedicated runner or against staging.
    /// </summary>
    internal const int NoiseFloorMs = 25;

    [Fact]
    public void AStepRegressesWhenItsP95IsMoreThanTwentyPercentAndTheNoiseFloorOverTheBaseline()
    {
        var baseline = new Dictionary<string, StepResult> { ["Book"] = new(100, 0, 10, 200, 250), ["Hold slot"] = new(100, 0, 10, 200, 250), ["Board read"] = new(100, 0, 5, 10, 12) };
        var now = new Dictionary<string, StepResult>
        {
            ["Book"] = new(100, 0, 10, 240, 250), ["Hold slot"] = new(100, 0, 10, 241, 250), ["Board read"] = new(100, 0, 5, 34, 40), ["Board push"] = new(9, 0, 1, 900, 900),
        };
        // Exactly 20 % is allowed; +240 % but 24 ms is runner noise; a new step is not compared.
        Assert.Equal(["Hold slot: p95 241 ms, baseline 200 ms (+21 %)"], Regressions(now, baseline));
        now["Board read"] = new(100, 0, 5, 35, 40);
        Assert.Equal(2, Regressions(now, baseline).Count);
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
