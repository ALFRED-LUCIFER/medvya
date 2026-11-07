using System.Diagnostics;
using System.Text.RegularExpressions;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Host.Api.Observability;

/// <summary>
/// OpenTelemetry with allowlist scrubbing (MDV-P0-018, PRD X2: no PHI in logs or telemetry).
/// Anything not known to be safe is dropped before any exporter sees it.
/// </summary>
public static partial class Telemetry
{
    public static WebApplicationBuilder AddMedvyaTelemetry(this WebApplicationBuilder builder)
    {
        var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        // Outside Development the scrubbed OpenTelemetry pipeline is the only log provider (#885): the default Console, Debug and EventSource
        // providers would write rendered messages and exception text to the container logs past the allowlist.
        if (!builder.Environment.IsDevelopment()) builder.Logging.ClearProviders();
        // The hosting "Request starting …" line prints the full URL including the query string.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = false; // keep the template, never the rendered values
            o.IncludeScopes = false;
            o.AddProcessor(new LogScrubber());
            if (appInsights is not null) o.AddAzureMonitorLogExporter(x => x.ConnectionString = appInsights);
        });

        builder.Services.AddOpenTelemetry().WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddProcessor(new TraceScrubber());
            if (appInsights is not null) t.AddAzureMonitorTraceExporter(x => x.ConnectionString = appInsights);
        }).WithMetrics(m =>
        {
            // Only Medvya's own meters, whose values are numbers and whose tags are opaque ids (check-in time, exit gate 3).
            m.AddMeter(Medvya.Scheduling.Infrastructure.FrontDeskService.MeterName);
            if (appInsights is not null) m.AddAzureMonitorMetricExporter(x => x.ConnectionString = appInsights);
        });

        // Every 409 problem logs its reason code (#912: a refused walk-in could not be diagnosed). The scrubber keeps only identifiers.
        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
        {
            if (c.ProblemDetails.Status != StatusCodes.Status409Conflict) return;
            var log = c.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Telemetry));
            var code = c.ProblemDetails.Extensions.TryGetValue("code", out var value) ? value as string : null;
            Conflict(log, code);
        });
        return builder;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request refused with 409 {ConflictCode}")]
    static partial void Conflict(ILogger logger, string? conflictCode);
}

/// <summary>Keeps only route-level span attributes; drops url.full, url.path, url.query and anything unknown.</summary>
public sealed class TraceScrubber : BaseProcessor<Activity>
{
    static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "http.request.method", "http.response.status_code", "http.route", "url.scheme", "server.address", "server.port",
        "network.protocol.version", "error.type", "otel.status_code",
    };

    public override void OnEnd(Activity data)
    {
        foreach (var tag in data.TagObjects.ToList())
            if (!Allowed.Contains(tag.Key)) data.SetTag(tag.Key, null);
    }
}

/// <summary>
/// Keeps the message template and opaque values only: GUIDs, plus numbers for a few known-safe names.
/// Every other parameter value is replaced, and exception messages (which can carry data) are dropped.
/// </summary>
public sealed partial class LogScrubber : BaseProcessor<LogRecord>
{
    public const string Redacted = "[redacted]";
    static readonly HashSet<string> SafeNumbers = new(StringComparer.Ordinal) { "StatusCode", "ElapsedMilliseconds", "Count", "Attempt" };
    /// <summary>Reason codes are lower-case snake_case constants ("no_free_slot"); any other value is redacted like the rest.</summary>
    [GeneratedRegex("^[a-z_]{1,40}$")]
    private static partial Regex ReasonCode();

    public override void OnEnd(LogRecord data)
    {
        if (data.Attributes is { } attributes)
            data.Attributes = attributes.Select(a => new KeyValuePair<string, object?>(a.Key, Keep(a) ? a.Value : Redacted)).ToList();
        if (data.Exception is { } ex)
        {
            data.Exception = null;
            data.Attributes = [.. data.Attributes ?? [], new("exception.type", ex.GetType().FullName)];
        }
    }

    static bool Keep(KeyValuePair<string, object?> a) =>
        a.Key == "{OriginalFormat}" || a.Value is Guid || (SafeNumbers.Contains(a.Key) && a.Value is int or long or double)
        || (a.Key == "ConflictCode" && a.Value is string code && ReasonCode().IsMatch(code));
}
