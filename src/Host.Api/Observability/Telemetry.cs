using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace Host.Api.Observability;

/// <summary>
/// OpenTelemetry with allowlist scrubbing (MDV-P0-018, PRD X2: no PHI in logs or telemetry).
/// Anything not known to be safe is dropped before any exporter sees it.
/// </summary>
public static class Telemetry
{
    public static WebApplicationBuilder AddMedvyaTelemetry(this WebApplicationBuilder builder)
    {
        var appInsights = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

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
        });
        return builder;
    }
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
public sealed class LogScrubber : BaseProcessor<LogRecord>
{
    public const string Redacted = "[redacted]";
    static readonly HashSet<string> SafeNumbers = new(StringComparer.Ordinal) { "StatusCode", "ElapsedMilliseconds", "Count", "Attempt" };

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
        a.Key == "{OriginalFormat}" || a.Value is Guid || (SafeNumbers.Contains(a.Key) && a.Value is int or long or double);
}
