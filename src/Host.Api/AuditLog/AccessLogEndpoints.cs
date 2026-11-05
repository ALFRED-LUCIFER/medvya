using System.Globalization;
using System.Security.Claims;
using System.Text;
using Medvya.Audit.Api;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Host.Api.AuditLog;

/// <summary>
/// Practice access log (F-12, MDV-P1-035): who opened or changed which record, when, why and with what outcome. Needs the Audit log
/// capability (practice admin, owner doctor, hospital IT; everyone else gets 403, which is itself audited as Denied). Events hold
/// ids and codes only; names come from the modules that own them. A patient's name is shown only on the per-patient route, whose
/// audit event carries that patient's id. The export is a CSV with exporter and time, without patient names.
/// </summary>
public static class AccessLogEndpoints
{
    const int DefaultPage = 50, MaxPage = 200, MaxExport = 10_000;

    public sealed record Filters(Guid? ActorUserId, string? Action, DateTimeOffset? From, DateTimeOffset? To, string? After, int? Limit);
    public sealed record ExportRequest(Guid? ActorUserId, Guid? PatientId, string? Action, DateTimeOffset? From, DateTimeOffset? To);

    sealed record Row(Guid Id, DateTimeOffset OccurredAt, Guid? ActorUserId, string? ActorName, string? Role, string Action, string ResourceType,
        Guid? PatientId, string? PatientMrn, string? PatientName, string Purpose, string Outcome);

    public static WebApplication MapAccessLog(this WebApplication app)
    {
        var g = app.MapGroup("/v1/audit/access-log").RequireCapability(Capability.AuditLog, Access.Read, Access.Full);

        g.MapGet("/", ([AsParameters] Filters f, IServiceProvider sp, CancellationToken ct) => List(null, f, sp, ct))
            .Audited("audit.read", "audit_log", "audit");
        g.MapGet("/patients/{patientId:guid}", (Guid patientId, [AsParameters] Filters f, IServiceProvider sp, CancellationToken ct) => List(patientId, f, sp, ct))
            .Audited("audit.read", "audit_log", "audit", patientRouteKey: "patientId");

        // POST, not GET: an export is an action that is audited on its own and never prefetched or cached.
        // shortcut: the export event does not record the filters used (they travel in the body); add them with an audit detail field.
        g.MapPost("/export", Export).Audited("audit.export", "audit_log", "audit");
        return app;
    }

    static async Task<IResult> List(Guid? patientId, Filters f, IServiceProvider sp, CancellationToken ct)
    {
        var limit = Math.Clamp(f.Limit ?? DefaultPage, 1, MaxPage);
        var page = await QueryAsync(sp, new AuditQuery(f.ActorUserId, patientId, Clean(f.Action), f.From, f.To, f.After, limit), ct);
        if (page is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["after"] = ["invalid"] });
        var rows = await Enrich(sp, page.Items, withPatientName: patientId is not null, ct);
        return Results.Ok(new { items = rows, next = page.Next, actions = page.Actions });
    }

    static async Task<IResult> Export([FromBody] ExportRequest? body, ClaimsPrincipal principal, HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        body ??= new(null, null, null, null, null);
        var page = await QueryAsync(sp, new AuditQuery(body.ActorUserId, body.PatientId, Clean(body.Action), body.From, body.To, null, MaxExport), ct);
        var rows = await Enrich(sp, page!.Items, withPatientName: false, ct);
        var exporter = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? (await sp.GetRequiredService<IStaffUsers>().DisplayNamesAsync([id], ct)).GetValueOrDefault(id) ?? id.ToString() : "unknown";
        var csv = Csv(rows, exporter, DateTimeOffset.UtcNow, truncated: page.Next is not null);
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", "access-log.csv");
    }

    static async Task<AuditPage?> QueryAsync(IServiceProvider sp, AuditQuery query, CancellationToken ct)
    {
        try { return await sp.GetRequiredService<IAuditTrail>().QueryAsync(query, ct); }
        catch (ArgumentException) { return null; } // a cursor that is not one of ours
    }

    static string? Clean(string? action) => string.IsNullOrWhiteSpace(action) ? null : action.Trim()[..Math.Min(action.Trim().Length, 64)];

    static async Task<IReadOnlyList<Row>> Enrich(IServiceProvider sp, IReadOnlyList<AuditEvent> events, bool withPatientName, CancellationToken ct)
    {
        var actors = events.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var names = await sp.GetRequiredService<IStaffUsers>().DisplayNamesAsync(actors, ct);
        // shortcut: the role shown is the person's current role at the practice, not the one they held when the event happened; add the role to the event.
        var roles = (await sp.GetRequiredService<IStaffRoster>().ListAsync(ct)).Where(r => r.UserId is not null)
            .GroupBy(r => r.UserId!.Value).ToDictionary(x => x.Key, x => x.First().Role);
        var patients = await sp.GetRequiredService<IPatientDirectory>().SummariesAsync([.. events.Where(e => e.PatientId is not null).Select(e => e.PatientId!.Value).Distinct()], ct);
        return [.. events.Select(e =>
        {
            var patient = e.PatientId is { } p && patients.TryGetValue(p, out var s) ? s : null;
            return new Row(e.Id, e.OccurredAt, e.ActorUserId, e.ActorUserId is { } a ? names.GetValueOrDefault(a) : null,
                e.ActorUserId is { } r && roles.TryGetValue(r, out var role) ? role.ToString() : null, e.Action, e.ResourceType,
                e.PatientId, patient?.Mrn, withPatientName ? patient?.FullName : null, e.Purpose, e.Outcome.ToString());
        })];
    }

    /// <summary>Watermark first and last (exporter and time); cells that a spreadsheet would run as a formula are neutralised.</summary>

    static string Csv(IReadOnlyList<Row> rows, string exporter, DateTimeOffset at, bool truncated)
    {
        var mark = $"Medvya practice access log. Exported by {exporter} at {at.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}. Confidential: contains staff activity on patient records.";
        var sb = new StringBuilder().Append(Cell(mark)).Append("\r\n")
            .Append("Time (UTC),Staff,Role,Action,Resource,Patient MRN,Purpose,Outcome\r\n");
        foreach (var r in rows)
            sb.AppendJoin(',', Cell(r.OccurredAt.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)), Cell(r.ActorName ?? r.ActorUserId?.ToString()), Cell(r.Role),
                Cell(r.Action), Cell(r.ResourceType), Cell(r.PatientMrn), Cell(r.Purpose), Cell(r.Outcome)).Append("\r\n");
        if (truncated) sb.Append(Cell($"Only the newest {MaxExport} events are included. Narrow the filters to see the rest.")).Append("\r\n");
        return sb.Append(Cell(mark)).Append("\r\n").ToString();
    }

    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value; // formula injection
        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
