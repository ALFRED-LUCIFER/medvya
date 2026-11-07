using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Medvya.IntegrationTests;

public class TelemetryRedactionTests
{
    // Synthetic values only (never valid IDs).
    const string Name = "Meera Iyer", Phone = "+91 98470 21346", EmiratesId = "784-1988-1234567-1";

    sealed class Snapshot(List<string> sink) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord r)
        {
            var sb = new StringBuilder($"{r.CategoryName}|{r.TraceId}|{r.FormattedMessage}|{r.Exception}|");
            foreach (var a in r.Attributes ?? []) sb.Append(a.Key).Append('=').Append(a.Value).Append(';');
            lock (sink) sink.Add(sb.ToString());
        }
    }

    // Simulates a developer mistake: logging patient data and an exception whose message carries an ID.
    sealed class LeakyMiddleware : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext ctx, RequestDelegate n) =>
            {
                var log = ctx.RequestServices.GetRequiredService<ILogger<LeakyMiddleware>>();
#pragma warning disable CA1848 // deliberately the plain logging API a developer might misuse
                log.LogWarning("Registered {PatientName} {Mobile} {EmiratesId} {RequestId}", Name, Phone, EmiratesId, Guid.NewGuid());
                log.LogError(new InvalidOperationException($"Duplicate identifier {EmiratesId}"), "Save failed with {StatusCode}", 409);
#pragma warning restore CA1848
                await n(ctx);
            });
            next(app);
        };
    }

    // The server ends its request span after the client already has the response; this marks when it has been exported.
    sealed class SignalWhenExported(ActivityTraceId traceId, TaskCompletionSource exported) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            if (data.TraceId == traceId && data.Kind == ActivityKind.Server) exported.TrySetResult();
        }
    }

    [Theory]
    // #885: outside Development the scrubbed OpenTelemetry provider is the only one; Console would print rendered messages.
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void OnlyTheScrubbedLogProviderRunsOutsideDevelopment(string environment, bool consoleAllowed)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        Host.Api.Observability.Telemetry.AddMedvyaTelemetry(builder);
        using var services = builder.Services.BuildServiceProvider();
        var providers = services.GetServices<Microsoft.Extensions.Logging.ILoggerProvider>().Select(p => p.GetType().Name).ToList();
        Assert.Contains("OpenTelemetryLoggerProvider", providers);
        Assert.Equal(consoleAllowed, providers.Contains("ConsoleLoggerProvider"));
        if (!consoleAllowed) Assert.Single(providers);
    }

    // Two 409 answers: one with a stable reason code, one whose "code" a developer filled with patient data by mistake.
    sealed class ConflictEndpoints : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext ctx, RequestDelegate n) =>
            {
                var code = ctx.Request.Path == "/conflict/coded" ? "no_free_slot" : ctx.Request.Path == "/conflict/leaky" ? Name : null;
                if (code is null) await n(ctx);
                else await Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(ctx);
            });
            next(app);
        };
    }

    /// <summary>#912: a 409 logs its reason code (an allowlisted identifier), so the next refusal is diagnosable; anything else stays redacted.</summary>
    [Fact]
    public async Task AConflictLogsItsReasonCodeAndNothingElse()
    {
        var logs = new List<string>();
        await using (var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .ConfigureLogging(l => l.AddOpenTelemetry(o => o.AddProcessor(new Snapshot(logs))))
            .ConfigureServices(s => s.AddSingleton<IStartupFilter, ConflictEndpoints>())))
        {
            var client = factory.CreateClient();
            Assert.Equal(System.Net.HttpStatusCode.Conflict, (await client.GetAsync(new Uri("/conflict/coded", UriKind.Relative))).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.Conflict, (await client.GetAsync(new Uri("/conflict/leaky", UriKind.Relative))).StatusCode);
        }

        var conflicts = logs.Where(l => l.Contains("ConflictCode=", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, l => l.Contains("ConflictCode=no_free_slot;", StringComparison.Ordinal));
        Assert.Contains(conflicts, l => l.Contains("ConflictCode=[redacted];", StringComparison.Ordinal));
        Assert.DoesNotContain("Meera", string.Join('\n', logs), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatientDataNeverReachesExportedLogsOrTraces()
    {
        var logs = new List<string>();
        var spans = new List<Activity>();
        // Our own trace id, so spans from other hosts in this process (tracing listeners are process-wide) are told apart.
        var traceId = ActivityTraceId.CreateRandom();
        var spanExported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .ConfigureLogging(l => l.AddOpenTelemetry(o => o.AddProcessor(new Snapshot(logs))))
            .ConfigureServices(s =>
            {
                s.AddSingleton<IStartupFilter, LeakyMiddleware>();
                s.AddOpenTelemetry().WithTracing(t => t.AddInMemoryExporter(spans).AddProcessor(new SignalWhenExported(traceId, spanExported)));
            }));

        var query = $"/health?name={Uri.EscapeDataString(Name)}&phone={Uri.EscapeDataString(Phone)}&eid={EmiratesId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(query, UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var res = await factory.CreateClient().SendAsync(request);
        res.EnsureSuccessStatusCode();
        await spanExported.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await factory.DisposeAsync(); // stop the host so nothing appends to logs/spans while we read them

        var exported = string.Join('\n', logs) + "\n" + string.Join('\n',
            spans.Select(a => a.DisplayName + " " + string.Join(';', a.TagObjects.Select(t => $"{t.Key}={t.Value}"))));

        foreach (var secret in new[] { "Meera", "98470", "21346", "784-1988", "1234567", "name=", "eid=" })
            Assert.DoesNotContain(secret, exported, StringComparison.Ordinal);

        // Still useful: templates, opaque ids, safe numbers, exception type, route and trace correlation survive.
        Assert.Contains("Registered {PatientName} {Mobile} {EmiratesId} {RequestId}", exported, StringComparison.Ordinal);
        Assert.Contains("StatusCode=409", exported, StringComparison.Ordinal);
        Assert.Contains("exception.type=System.InvalidOperationException", exported, StringComparison.Ordinal);
        Assert.Contains(spans, a => a.TraceId == traceId && Equals(a.GetTagItem("http.route"), "/health"));
        Assert.Contains(logs, l => !l.Contains("|00000000000000000000000000000000|", StringComparison.Ordinal));
    }
}
