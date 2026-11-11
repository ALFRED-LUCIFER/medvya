using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Medvya.Encounters.Api;

/// <summary>Who the encounter is for, as the identity banner shows it on every clinical screen (MDV-P2-001). The language only as recorded (MDV-P2-032).</summary>
public sealed record BannerPatient(Guid Id, string FullName, string Mrn, DateOnly DateOfBirth, int AgeYears, SexAtBirth SexAtBirth, string? PreferredLanguage);

/// <summary>
/// The encounter with its patient's identity and treatment consent. Allergies are not here yet: they are recorded from MDV-P2-005, and until
/// then the banner says they are not recorded (never "none"). Times show in <see cref="TimeZone"/>, the encounter facility's zone.
/// </summary>
public sealed record EncounterBanner(EncounterView Encounter, BannerPatient Patient, bool TreatmentConsent, string TimeZone, AllergyBanner Allergies);

public sealed record AllergyFlagsRequest(IReadOnlyList<Guid>? PatientIds);

/// <summary>
/// Patient 360 (MDV-P2-004): the banner identity and the verified chart, each fact with its source. The life stage and the history sections
/// it offers (MDV-P2-032) are worked out from the date of birth on the facility's date.
/// </summary>
public sealed record Patient360(BannerPatient Patient, bool TreatmentConsent, string TimeZone, PatientChart Chart, LifeStage LifeStage, IReadOnlyList<HistorySection> HistorySections);

/// <summary>A timeline entry as the screen shows it: the author's name, never an id the screen would look up.</summary>
public sealed record TimelineEntryView(Guid Id, TimelineKind Kind, DateTimeOffset At, string Code, string? Title, string? Detail, string? Author, ChartSource Source);

/// <summary><see cref="Next"/> goes back as <c>before</c> for "Load earlier"; null at the end.</summary>
public sealed record TimelineResponse(IReadOnlyList<TimelineEntryView> Entries, string? Next, string TimeZone);

public sealed record TrendsResponse(IReadOnlyList<TrendView> Trends, string TimeZone);

public static class EncounterEndpoints
{
    /// <summary>
    /// Encounters (MDV-P2-001). Signed in with a role that treats patients at the session's facility; the use case then checks clinical
    /// access to the patient at the visit's facility (role, care relationship, MDV-P2-024) and writes the audit event with the patient.
    /// A refusal, a missing encounter or a conflict is audited here. Ids only in paths; nothing about the patient in a URL.
    /// </summary>
    public static IEndpointRouteBuilder MapEncounterEndpoints(this IEndpointRouteBuilder app)
    {
        var encounters = app.MapGroup("/v1/encounters").RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign);

        encounters.MapPost("/", async ([FromBody] StartEncounter? body, [FromServices] IEncounters service, CancellationToken ct) =>
                body is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["appointmentId"] = ["required"] })
                : await service.StartAsync(body, ct) is { } started ? Results.Ok(started) : Results.NotFound())
            .AuditedByUseCase("encounter.start", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        // Opening the encounter shows who it is for: audited by the use case with the patient, like opening a record.
        encounters.MapGet("/{encounterId:guid}", Banner).AuditedByUseCase("encounter.read", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        encounters.MapPost("/{encounterId:guid}/end", async (Guid encounterId, [FromServices] IEncounters service, CancellationToken ct) =>
                await service.EndAsync(encounterId, ct) is { } ended ? Results.Ok(ended) : Results.NotFound())
            .AuditedByUseCase("encounter.end", "encounter", AuditPurpose.Treatment).AddEndpointFilter(MapErrors);

        // Patient 360 (E-01). Policy: a role with the Patient 360 capability (reception has none: refused, audited as denied). The use case
        // then checks the care relationship at the session's facility and audits the read with the patient and purpose treatment.
        app.MapGet("/v1/patients/{patientId:guid}/chart", Chart)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.chart.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        // History sections (MDV-P2-032): a doctor or a nurse (vitals and allergies entry at Full); the text is in the body, never the URL.
        app.MapPut("/v1/patients/{patientId:guid}/history/{section}", SaveHistory)
            .RequireCapability(Capability.VitalsAllergiesEntry, Access.Full)
            .AuditedByUseCase("patient.history.save", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);

        // Today's allergy flag per visit (MDV-P2-002 follow-up): only for patients the caller has a care relationship with.
        app.MapPost("/v1/chart/allergy-flags", AllergyFlags)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AddEndpointFilter(MapErrors);

        // E-02. Filters and the cursor are codes and ids, never patient data, so they may travel in the query string.
        app.MapGet("/v1/patients/{patientId:guid}/timeline", Timeline)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.timeline.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapGet("/v1/patients/{patientId:guid}/trends", Trends)
            .RequireCapability(Capability.Patient360Chart, Access.Read, Access.Limited, Access.Full, Access.Sign)
            .AuditedByUseCase("patient.trends.read", "patient", AuditPurpose.Treatment, patientRouteKey: "patientId")
            .AddEndpointFilter(MapErrors);
        app.MapDocumentEndpoints();
        app.MapReconciliationEndpoints();
        app.MapProblemEndpoints();
        app.MapChildHealthEndpoints();
        app.MapGeriatricEndpoints();
        app.MapChronicCareEndpoints();
        app.MapNoteEndpoints();
        return app;
    }

    static async Task<IResult> Timeline(Guid patientId, string? kinds, string? source, string? before, int? limit, [FromServices] IPatientTimeline timeline,
        [FromServices] IPatientDirectory patients, [FromServices] IStaffUsers users, [FromServices] IFacilityDirectory facilities, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        var parsedKinds = new List<TimelineKind>();
        foreach (var k in (kinds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<TimelineKind>(k, out var kind) || !Enum.IsDefined(kind)) return BadFilter();
            parsedKinds.Add(kind);
        }
        ChartSource? parsedSource = null;
        if (source is not null)
        {
            if (!Enum.TryParse<ChartSource>(source, out var s) || !Enum.IsDefined(s)) return BadFilter();
            parsedSource = s;
        }
        if (!TryCursor(before, out var cursor)) return BadFilter();
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        var page = await timeline.PageAsync(patientId, facilityId, new(parsedKinds.ToHashSet(), parsedSource, cursor, limit ?? TimelineQuery.PageSize), ct);
        var names = await users.DisplayNamesAsync([.. page.Entries.Where(e => e.AuthorUserId is not null).Select(e => e.AuthorUserId!.Value).Distinct()], ct);
        var last = page.HasMore ? page.Entries[^1] : null;
        return Results.Ok(new TimelineResponse(
            [.. page.Entries.Select(e => new TimelineEntryView(e.Id, e.Kind, e.At, e.Code, e.Title, e.Detail, e.AuthorUserId is { } a ? names.GetValueOrDefault(a) : null, e.Source))],
            last is null ? null : $"{last.At.UtcTicks}.{last.Id}", (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone));
    }

    static IResult BadFilter() => Results.ValidationProblem(new Dictionary<string, string[]> { ["filter"] = ["invalid"] });

    static bool TryCursor(string? text, out TimelineCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(text)) return true;
        var parts = text.Split('.', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || ticks < 0 || ticks > DateTime.MaxValue.Ticks || !Guid.TryParse(parts[1], out var id)) return false;
        cursor = new(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    static async Task<IResult> Trends(Guid patientId, [FromServices] IPatientTimeline timeline, [FromServices] IPatientDirectory patients, [FromServices] IFacilityDirectory facilities,
        [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        return Results.Ok(new TrendsResponse(await timeline.TrendsAsync(patientId, facilityId, ct),
            (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone));
    }

    static async Task<IResult> Chart(Guid patientId, [FromServices] IPatientChart chart, [FromServices] IPatientDirectory patients, [FromServices] IPatientConsents consents,
        [FromServices] IStaffAccess staff, [FromServices] IFacilityDirectory facilities, [FromServices] ICurrentFacility current, [FromServices] TimeProvider clock, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        // Existence only comes before the access check; nothing about the patient is returned until it passes.
        if ((await patients.SummariesAsync([patientId], ct)).GetValueOrDefault(patientId) is not { } p) return Results.NotFound();
        var facts = await chart.GetAsync(patientId, facilityId, ct);
        var today = CountryRules.For(await staff.TenantCountryAsync(ct) ?? throw new InvalidOperationException("No tenant country.")).Today(clock.GetUtcNow());
        var stage = LifeStages.Of(p.DateOfBirth, today, openPregnancy: false); // shortcut: no pregnancy record until MDV-P2-034, which passes the open one here
        return Results.Ok(new Patient360(BannerOf(p, today), await consents.IsGrantedAsync(p.Id, ConsentType.Treatment, cancellationToken: ct),
            (await facilities.FindAsync(facilityId, ct) ?? throw new InvalidOperationException("Session facility not in this tenant.")).TimeZone, facts,
            stage, HistoryRules.Sections(stage, female: p.SexAtBirth == SexAtBirth.Female)));
    }

    internal static BannerPatient BannerOf(PatientSummary p, DateOnly today) =>
        new(p.Id, p.FullName, p.Mrn, p.DateOfBirth, CountryRules.AgeOn(today, p.DateOfBirth), p.SexAtBirth, p.PreferredLanguage);

    // The section is a name, parsed here so an unknown one is a refusal the audit filter records (route binding would answer before it).
    static async Task<IResult> SaveHistory(Guid patientId, string section, [FromBody] SaveHistory? body, [FromServices] IPatientHistory history,
        [FromServices] IPatientDirectory patients, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (!Enum.GetNames<HistorySection>().Contains(section)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["section"] = ["invalid"] });
        if (!(await patients.SummariesAsync([patientId], ct)).ContainsKey(patientId)) return Results.NotFound();
        return Results.Ok(new { items = await history.SaveAsync(patientId, facilityId, Enum.Parse<HistorySection>(section), body ?? new SaveHistory(null), ct) });
    }

    static async Task<IResult> Banner(Guid encounterId, [FromServices] IEncounters service, [FromServices] IPatientDirectory patients, [FromServices] IPatientConsents consents,
        [FromServices] IStaffAccess staff, [FromServices] IFacilityDirectory facilities, [FromServices] TimeProvider clock, CancellationToken ct)
    {
        if (await service.GetAsync(encounterId, ct) is not { } encounter) return Results.NotFound();
        if ((await patients.SummariesAsync([encounter.PatientId], ct)).GetValueOrDefault(encounter.PatientId) is not { } p) return Results.NotFound();
        var today = CountryRules.For(await staff.TenantCountryAsync(ct) ?? throw new InvalidOperationException("No tenant country.")).Today(clock.GetUtcNow());
        return Results.Ok(new EncounterBanner(encounter, BannerOf(p, today),
            await consents.IsGrantedAsync(p.Id, ConsentType.Treatment, cancellationToken: ct),
            (await facilities.FindAsync(encounter.FacilityId, ct) ?? throw new InvalidOperationException("Encounter facility not in this tenant.")).TimeZone,
            await service.AllergiesAsync(encounterId, ct) ?? throw new InvalidOperationException("Encounter vanished.")));
    }

    // ids in the body, never the URL; a patient the caller may not treat is simply absent from the answer.
    static async Task<IResult> AllergyFlags([FromBody] AllergyFlagsRequest? body, [FromServices] IPatientChart chart, [FromServices] ICurrentFacility current, CancellationToken ct)
    {
        if (current.FacilityId is not { } facilityId) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        if (body?.PatientIds is not { Count: > 0 and <= IPatientChart.MaxFlags } ids) return Results.ValidationProblem(new Dictionary<string, string[]> { ["patientIds"] = ["invalid"] });
        return Results.Ok(new { items = await chart.AllergyFlagsAsync(ids, facilityId, ct) });
    }

    // Inside the audit filter (added after it), so the row records the answer the caller gets.
    static async ValueTask<object?> MapErrors(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ClinicalAccessDeniedException e)
        {
            return ClinicalAccessProblem.For(e);
        }
        catch (HistoryValidationException e)
        {
            return Results.ValidationProblem(e.Errors.GroupBy(x => x.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToArray()));
        }
        catch (OpenEncounterException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another consultation is open",
                extensions: new Dictionary<string, object?> { ["code"] = "open_encounter", ["encounterId"] = e.EncounterId });
        }
        catch (EncounterEndedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This consultation has ended", extensions: new Dictionary<string, object?> { ["code"] = "ended" });
        }
        catch (VisitTransitionException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Visit cannot make this move", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
        catch (AppointmentNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
