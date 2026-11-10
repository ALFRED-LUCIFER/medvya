using Medvya.Tenancy.Api;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Medvya.Audit.Api;

/// <summary>
/// Practice access log (F-12, MDV-P1-035): who opened or changed which record, when, why and with what outcome. The role shown is the one held at event time; older rows show none. Needs the Audit log
/// capability (practice admin, owner doctor, hospital IT; everyone else gets 403, which is itself audited as Denied). Events hold
/// ids and codes only; names come from the modules that own them. A patient's name is shown only on the per-patient route, whose
/// audit event carries that patient's id. The export is a CSV with exporter and time, without patient names.
/// </summary>
public static class AccessLogEndpoints
{
    const int DefaultPage = 50, MaxPage = 200, MaxExport = 10_000;

    public sealed record Filters(Guid? ActorUserId, string? Action, DateTimeOffset? From, DateTimeOffset? To, string? After, int? Limit);
    public sealed record ExportRequest(Guid? ActorUserId, Guid? PatientId, string? Action, DateTimeOffset? From, DateTimeOffset? To);

    sealed record Row(Guid Id, DateTimeOffset OccurredAt, Guid? ActorUserId, string? ActorName, string? Role, string? Location, string Action, string ResourceType,
        Guid? PatientId, string? PatientMrn, string? PatientName, string Purpose, string Outcome, string? Detail);

    public static WebApplication MapAccessLog(this WebApplication app)
    {
        var g = app.MapGroup("/v1/audit/access-log").RequireCapability(Capability.AuditLog, Access.Read, Access.Full);

        g.MapGet("/", ([AsParameters] Filters f, ClaimsPrincipal user, ICurrentFacility facility, IServiceProvider sp, CancellationToken ct) => List(null, f, user, facility, sp, ct))
            .Audited("audit.read", "audit_log", "audit");
        g.MapGet("/patients/{patientId:guid}", (Guid patientId, [AsParameters] Filters f, ClaimsPrincipal user, ICurrentFacility facility, IServiceProvider sp, CancellationToken ct) => List(patientId, f, user, facility, sp, ct))
            .Audited("audit.read", "audit_log", "audit", patientRouteKey: "patientId");

        // POST, not GET: an export is an action that is audited on its own and never prefetched or cached.
        g.MapPost("/export", Export).Audited("audit.export", "audit_log", "audit");
        return app;
    }

    /// <summary>
    /// Hospital IT works at one facility and sees only that facility's events; practice admins and owner doctors see the whole practice.
    /// Events from before the facility was recorded show to the whole-practice roles only.
    /// shortcut: facility-limited means the hospital IT role; a per-membership facility limit needs a membership flag, add it when another role is limited.
    /// </summary>
    static async Task<Guid?> ScopeOf(IServiceProvider sp, ICurrentFacility facility, CancellationToken ct) =>
        await sp.GetRequiredService<IStaffAccess>().CurrentRoleAsync(ct) == Role.HospitalIt ? facility.FacilityId ?? Guid.Empty : null;

    static async Task<IResult> List(Guid? patientId, Filters f, ClaimsPrincipal user, ICurrentFacility facility, IServiceProvider sp, CancellationToken ct)
    {
        var limit = Math.Clamp(f.Limit ?? DefaultPage, 1, MaxPage);
        var page = await QueryAsync(sp, new AuditQuery(f.ActorUserId, patientId, Clean(f.Action), f.From, f.To, f.After, limit) { FacilityId = await ScopeOf(sp, facility, ct) }, ct);
        if (page is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["after"] = ["invalid"] });
        var rows = await Enrich(sp, page.Items, withPatientName: patientId is not null, ct);
        return Results.Ok(new { items = rows, next = page.Next, actions = page.Actions });
    }

    static async Task<IResult> Export([FromBody] ExportRequest? body, ClaimsPrincipal principal, ICurrentFacility facility, HttpContext http, IServiceProvider sp, CancellationToken ct)
    {
        body ??= new(null, null, null, null, null);
        // The filters used travel with the export event: opaque ids, an action code and dates, nothing the log does not already show.
        http.Items["audit.detail"] = string.Join("; ", new[] { ("staff", body.ActorUserId?.ToString()), ("patient", body.PatientId?.ToString()), ("action", Clean(body.Action)),
            ("from", body.From?.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)), ("to", body.To?.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)) }
            .Where(x => x.Item2 is not null).Select(x => $"{x.Item1}={x.Item2}").DefaultIfEmpty("none"));
        var page = await QueryAsync(sp, new AuditQuery(body.ActorUserId, body.PatientId, Clean(body.Action), body.From, body.To, null, MaxExport) { FacilityId = await ScopeOf(sp, facility, ct) }, ct);
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
        // The location where the record was read (the shared patient record, MDV-P1-072); older rows show none.
        var sites = sp.GetRequiredService<IFacilityDirectory>();
        var locations = new Dictionary<Guid, string?>();
        foreach (var f in events.Where(e => e.FacilityId is not null).Select(e => e.FacilityId!.Value).Distinct()) locations[f] = (await sites.FindAsync(f, ct))?.Name;
        var patients = await sp.GetRequiredService<IPatientDirectory>().SummariesAsync([.. events.Where(e => e.PatientId is not null).Select(e => e.PatientId!.Value).Distinct()], ct);
        return [.. events.Select(e =>
        {
            var patient = e.PatientId is { } p && patients.TryGetValue(p, out var s) ? s : null;
            return new Row(e.Id, e.OccurredAt, e.ActorUserId, e.ActorUserId is { } a ? names.GetValueOrDefault(a) : null,
                e.Role, e.FacilityId is { } site ? locations[site] : null, e.Action, e.ResourceType, e.PatientId, patient?.Mrn, withPatientName ? patient?.FullName : null, e.Purpose, e.Outcome.ToString(), e.Detail);
        })];
    }

    /// <summary>Watermark first and last (exporter and time); cells that a spreadsheet would run as a formula are neutralised.</summary>

    static string Csv(IReadOnlyList<Row> rows, string exporter, DateTimeOffset at, bool truncated)
    {
        var mark = $"Medvya practice access log. Exported by {exporter} at {at.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}. Confidential: contains staff activity on patient records.";
        var sb = new StringBuilder().Append(Cell(mark)).Append("\r\n")
            .Append("Time (UTC),Staff,Role at the time,Location,Action,Resource,Patient MRN,Purpose,Outcome,Details\r\n");
        foreach (var r in rows)
            sb.AppendJoin(',', Cell(r.OccurredAt.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)), Cell(r.ActorName ?? r.ActorUserId?.ToString()), Cell(r.Role), Cell(r.Location),
                Cell(r.Action), Cell(r.ResourceType), Cell(r.PatientMrn), Cell(r.Purpose), Cell(r.Outcome), Cell(r.Detail)).Append("\r\n");
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
