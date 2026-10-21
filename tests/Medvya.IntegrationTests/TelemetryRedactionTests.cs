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

    [Fact]
    public async Task PatientDataNeverReachesExportedLogsOrTraces()
    {
        var logs = new List<string>();
        var spans = new List<Activity>();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .ConfigureLogging(l => l.AddOpenTelemetry(o => o.AddProcessor(new Snapshot(logs))))
            .ConfigureServices(s =>
            {
                s.AddSingleton<IStartupFilter, LeakyMiddleware>();
                s.AddOpenTelemetry().WithTracing(t => t.AddInMemoryExporter(spans));
            }));

        var query = $"/health?name={Uri.EscapeDataString(Name)}&phone={Uri.EscapeDataString(Phone)}&eid={EmiratesId}";
        var res = await factory.CreateClient().GetAsync(new Uri(query, UriKind.Relative));
        res.EnsureSuccessStatusCode();
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush();
        await factory.DisposeAsync(); // stop the host so nothing appends to logs/spans while we read them

        var exported = string.Join('\n', logs) + "\n" + string.Join('\n',
            spans.Select(a => a.DisplayName + " " + string.Join(';', a.TagObjects.Select(t => $"{t.Key}={t.Value}"))));

        foreach (var secret in new[] { "Meera", "98470", "21346", "784-1988", "1234567", "name=", "eid=" })
            Assert.DoesNotContain(secret, exported, StringComparison.Ordinal);

        // Still useful: templates, opaque ids, safe numbers, exception type, route and trace correlation survive.
        Assert.Contains("Registered {PatientName} {Mobile} {EmiratesId} {RequestId}", exported, StringComparison.Ordinal);
        Assert.Contains("StatusCode=409", exported, StringComparison.Ordinal);
        Assert.Contains("exception.type=System.InvalidOperationException", exported, StringComparison.Ordinal);
        Assert.Contains(spans, a => Equals(a.GetTagItem("http.route"), "/health"));
        Assert.Contains(logs, l => !l.Contains("|00000000000000000000000000000000|", StringComparison.Ordinal));
    }
}
