using System.Text.Json.Serialization;
using Medvya.Audit.Api;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Patients.Api;

/// <summary>Search text travels in the POST body: names, phones and IDs are PHI and never go in a URL or query string.</summary>
public sealed record SearchRequest(string? Query = null, DirectoryFilter Filter = DirectoryFilter.All, int Page = 1);

/// <summary>What the signed-in clinician may do with a ⌘K result (MDV-P2-003): open it, ask for access (or already did), or nothing (the role cannot treat patients).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LookupAccess>))]
public enum LookupAccess { Open, CanRequest, RequestPending, NoAccess }

/// <summary>A ⌘K result: initials, age and the MRN tail only (never a name, phone or identifier) and whether the chart may be opened.</summary>
public sealed record LookupRow(Guid Id, string Initials, int AgeYears, bool AgeEstimated, string MrnTail, LookupAccess Access);

/// <summary>National and insurance numbers are masked to their last four characters (data-classification.md: masked in UI).</summary>
public sealed record IdentifierView(IdentifierScheme Scheme, string LastFour, DateOnly? ExpiresOn);

public sealed record PayerView(PayerKind Kind, string? PayerName, string? Plan, string? MemberNumberLastFour, DateOnly? ValidTo, string? Network);

/// <summary>
/// An opened record for the front desk: registration data only, never clinical data. <see cref="RegisteredOn"/> is the calendar
/// date in the country's time zone (one per country for India and the UAE).
/// </summary>
public sealed record PatientDetails(
    Guid Id, string Mrn, string FullName, string? ArabicName, DateOnly DateOfBirth, bool DateOfBirthEstimated, int AgeYears, SexAtBirth SexAtBirth,
    string? GenderIdentity, string? PreferredLanguage, string? Nationality, string Mobile, string? AlternatePhone, string? Email,
    PatientAddress? Address, RegistrationStatus Status, DateOnly RegisteredOn, IReadOnlyList<IdentifierView> Identifiers, PayerView? Payer,
    ContactView? EmergencyContact, ContactView? Guardian);

/// <summary>An emergency contact or guardian: a name and number to call (MDV-P1-020).</summary>
public sealed record ContactView(string Name, RelationshipKind Relationship, string Phone);

/// <summary>The full numbers, returned only by the audited reveal (checking the patient's card or a claim at the desk).</summary>
public sealed record RevealedIdentifiers(IReadOnlyList<RevealedIdentifier> Identifiers, string? MemberNumber);

public sealed record RevealedIdentifier(IdentifierScheme Scheme, string Value);

/// <summary>What the desk has typed so far; identifiers that are not complete yet are skipped, not rejected.</summary>
public sealed record DuplicateCheckRequest(string? FullName = null, DateOnly? DateOfBirth = null, IReadOnlyList<IdentifierInput>? Identifiers = null);

/// <summary>The details that complete a quick registration (MDV-P1-015): address, payer or both.</summary>
public sealed record CompletionRequest(PatientAddress? Address = null, PayerInput? Payer = null, ContactInput? EmergencyContact = null);

/// <summary>A registration from the desk (P-02). Facility and country are not here: the server takes them from the session.</summary>
public sealed record RegisterRequest
{
    public bool Quick { get; init; }
    public string FullName { get; init; } = "";
    public string? ArabicName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public int? AgeYears { get; init; }
    public SexAtBirth SexAtBirth { get; init; } = (SexAtBirth)(-1); // missing = invalid, never defaulted to a sex
    public string? GenderIdentity { get; init; }
    public string? PreferredLanguage { get; init; }
    public string? Nationality { get; init; }
    public string Mobile { get; init; } = "";
    public string? AlternatePhone { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<IdentifierInput> Identifiers { get; init; } = [];
    public PatientAddress? Address { get; init; }
    public PayerInput? Payer { get; init; }
    public ContactInput? EmergencyContact { get; init; }
    public ContactInput? Guardian { get; init; }
    public DuplicateResolution? Duplicates { get; init; }
    public ConsentSet? Consents { get; init; }

    internal RegisterPatient ToCommand(Guid facilityId, string countryCode) => new()
    {
        FacilityId = facilityId, CountryCode = countryCode, Quick = Quick, FullName = FullName, ArabicName = ArabicName,
        DateOfBirth = DateOfBirth, AgeYears = AgeYears, SexAtBirth = SexAtBirth, GenderIdentity = GenderIdentity,
        PreferredLanguage = PreferredLanguage, Nationality = Nationality, Mobile = Mobile, AlternatePhone = AlternatePhone, Email = Email,
        Identifiers = Identifiers, Address = Address, Payer = Payer, EmergencyContact = EmergencyContact, Guardian = Guardian, Duplicates = Duplicates, Consents = Consents,
    };
}

/// <summary>Two records of the active facility to review for a merge; the older one survives.</summary>
public sealed record MergeRequestBody(Guid PatientId, Guid OtherPatientId);

public static class PatientEndpoints
{
    /// <summary>Per-user rate limit for search endpoints (registered by the host with <see cref="UserRateLimit.AddUserRateLimit"/>).</summary>
    public const string SearchRateLimit = "patient-search";
    /// <summary>Per-user rate limit for opening records and revealing their numbers (registered by the host, like search).</summary>
    public const string RecordRateLimit = "patient-record";
    /// <summary>Per-user rate limit for the duplicate check, which runs while the desk types (registered by the host, like search).</summary>
    public const string DuplicateCheckRateLimit = "patient-duplicate-check";
    const string Purpose = "registration";

    /// <summary>
    /// Front-desk patient endpoints (MDV-P1-018, MDV-P1-016). Reading needs Register / book patients at Read or Full, registering
    /// at Full. Reads are audited once they answer, before the answer is sent; a registration audits inside its own transaction.
    /// </summary>
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var patients = app.MapGroup("/v1/patients").RequireCapability(Capability.RegisterBookPatients, Access.Read, Access.Full);

        patients.MapPost("/search", Search)
            .RequireUserRateLimit(SearchRateLimit) // before the audit filter: a refused search reads nothing
            .Audited("patient.search", "patient", Purpose);

        // Global search (MDV-P2-003): the same audited, rate-limited search, for treatment, with the care-relationship verdict per row.
        patients.MapPost("/lookup", Lookup)
            .RequireUserRateLimit(SearchRateLimit)
            .Audited("patient.search", "patient", AuditPurpose.Treatment);

        // shortcut: per-user limits only; no alert on unusual read volume (e.g. one user opening hundreds of records a day) until the
        // practice access log exists (MDV-P1-035).
        patients.MapGet("/{patientId:guid}", Get)
            .RequireUserRateLimit(RecordRateLimit)
            .Audited("patient.read", "patient", Purpose, patientRouteKey: "patientId");

        // POST, not GET: a reveal is an action that is audited on its own, never prefetched or cached.
        patients.MapPost("/{patientId:guid}/identifiers/reveal", Reveal)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .RequireUserRateLimit(RecordRateLimit)
            .Audited("patient.identifiers.reveal", "patient", Purpose, patientRouteKey: "patientId");

        patients.MapPost("/duplicate-check", DuplicateCheck)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .RequireUserRateLimit(DuplicateCheckRateLimit)
            .Audited("patient.duplicate_check", "patient", Purpose);

        patients.MapPost("/", Register)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.register", "patient", Purpose);

        // Only the active facility's patients: another facility's is 404 and changes nothing (audited NotFound by the filter).
        patients.MapPost("/{patientId:guid}/completion", Complete)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.update", "patient", Purpose, patientRouteKey: "patientId");

        // The staff screen's current state per consent. A read of the record's consent data, so audited like opening the record.
        patients.MapGet("/{patientId:guid}/consents", async (Guid patientId, [FromServices] IPatientConsents consents, CancellationToken ct) =>
                await consents.CurrentAsync(patientId, ct) is { } decisions ? Results.Ok(new { policyVersion = ConsentPolicy.CurrentVersion, decisions }) : Results.NotFound())
            .Audited("patient.consent.read", "patient", Purpose, patientRouteKey: "patientId");

        // A changed decision is a new receipt; the old ones stay. Same facility rule as completion: another facility's patient is 404.
        patients.MapPost("/{patientId:guid}/consents", ChangeConsents)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.consent.record", "patient", Purpose, patientRouteKey: "patientId");

        MapMergeEndpoints(patients);
        return app;
    }

    /// <summary>
    /// Merge review (P-03, MDV-P1-017). Asking for a review and "not the same" are desk work (Register / book patients, Full); approving
    /// is clinician work (Clinical notes, Sign); undoing is admin work (Staff, roles, integrations, Config). The rules that make a merge
    /// impossible without its approvers live in the use case, not here.
    /// </summary>
    static void MapMergeEndpoints(RouteGroupBuilder patients)
    {
        patients.MapGet("/merge-requests", async ([FromServices] IPatientMerges merges, CancellationToken ct) => Results.Ok(new { requests = await merges.QueueAsync(ct) }))
            .Audited("patient.merge.queue", "merge_request", Purpose);
        patients.MapGet("/merge-requests/{requestId:guid}", async (Guid requestId, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                await merges.GetAsync(requestId, ct) is { } r ? Results.Ok(r) : Results.NotFound())
            .Audited("patient.merge.read", "merge_request", Purpose);
        patients.MapGet("/{patientId:guid}/merges", async (Guid patientId, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                await merges.HistoryAsync(patientId, ct) is { } h ? Results.Ok(new { merges = h }) : Results.NotFound())
            .Audited("patient.merge.history", "patient", Purpose, patientRouteKey: "patientId");

        patients.MapPost("/merge-requests", async ([FromBody] MergeRequestBody? body, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                body is null ? Invalid(new([new("merge", "required")])) : await Merge(async () => await merges.RequestAsync(body.PatientId, body.OtherPatientId, ct) is { } id ? Results.Ok(new { id }) : Results.NotFound()))
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.merge.request", "merge_request", Purpose);
        patients.MapPost("/merge-requests/{requestId:guid}/not-same", async (Guid requestId, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                await Merge(async () => await merges.NotSameAsync(requestId, ct) ? Results.NoContent() : Results.NotFound()))
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.merge.not_same", "merge_request", Purpose);
        patients.MapPost("/merge-requests/{requestId:guid}/request-approval", async (Guid requestId, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                await Merge(async () => await merges.RequestApprovalAsync(requestId, ct) ? Results.NoContent() : Results.NotFound()))
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.merge.request_approval", "merge_request", Purpose);
        patients.MapPost("/merge-requests/{requestId:guid}/approve", async (Guid requestId, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                await Merge(async () => await merges.ApproveAsync(requestId, ct) is { } status ? Results.Ok(new { status }) : Results.NotFound()))
            .RequireCapability(Capability.ClinicalNotes, Access.Sign)
            .AuditedByUseCase("patient.merge.approve", "merge_request", Purpose);
        patients.MapPost("/merge-requests/{requestId:guid}/undo", async (Guid requestId, [FromServices] IPatientMerges merges, CancellationToken ct) =>
                await Merge(async () => await merges.UndoAsync(requestId, ct) ? Results.NoContent() : Results.NotFound()))
            .RequireCapability(Capability.StaffRolesIntegrations, Access.Config)
            .AuditedByUseCase("patient.merge.undo", "merge_request", Purpose);
    }

    static async Task<IResult> Merge(Func<Task<IResult>> run)
    {
        try
        {
            return await run();
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
    }

    static async Task<IResult> ChangeConsents(Guid patientId, [FromBody] ConsentSet? request, [FromServices] IPatientConsents consents, CancellationToken ct)
    {
        try
        {
            return await consents.ChangeAsync(patientId, request!, ct) is { } decisions ? Results.Ok(new { decisions }) : Results.NotFound();
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
    }

    static async Task<IResult> DuplicateCheck([FromBody] DuplicateCheckRequest? request, [FromServices] IStaffAccess staff, [FromServices] IPatientRegistry registry, CancellationToken ct)
    {
        request ??= new();
        if (await staff.TenantCountryAsync(ct) is not { } country) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        // While typing, a name shorter than two letters is not an error: there is simply nothing to compare yet.
        var name = request.FullName?.Trim() ?? "";
        if (name.Length > 200) return Invalid(new PatientValidationException([new("fullName", "too_long")]));
        var candidates = name.Count(char.IsLetter) < 2 && (request.Identifiers ?? []).Count == 0
            ? []
            : await registry.FindDuplicatesAsync(new(country, name, request.DateOfBirth, request.Identifiers ?? []), ct);
        return Results.Ok(new { candidates });
    }

    static async Task<IResult> Register([FromBody] RegisterRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, [FromServices] IStaffAccess staff, [FromServices] ICurrentFacility facility,
        [FromServices] IPatientRegistry registry, CancellationToken ct)
    {
        if (await staff.TenantCountryAsync(ct) is not { } country || facility.FacilityId is not { } facilityId)
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        try
        {
            // Facility and country come from the session and the tenant, never from the request body.
            return Results.Ok(await registry.RegisterAsync(request.ToCommand(facilityId, country), key, ct));
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
        catch (IdempotencyKeyException e)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Idempotency-Key refused", extensions: new Dictionary<string, object?> { ["code"] = e.Code });
        }
    }

    static async Task<IResult> Complete(Guid patientId, [FromBody] CompletionRequest? request, [FromServices] IPatientRegistry registry, CancellationToken ct)
    {
        request ??= new();
        try
        {
            return await registry.CompleteRegistrationAsync(patientId, request.Address, request.Payer, request.EmergencyContact, ct) is { } status
                ? Results.Ok(new { status })
                : Results.NotFound();
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(); // #889: someone else changed this record meanwhile; nothing was saved
        }
    }

    static IResult Conflict() => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The record was changed by someone else. Reload and try again.",
        extensions: new Dictionary<string, object?> { ["code"] = "changed" });

    static async Task<IResult> Search([FromBody] SearchRequest? request, [FromServices] IStaffAccess staff, [FromServices] IPatientRegistry registry, CancellationToken ct)
    {
        request ??= new();
        if (await staff.TenantCountryAsync(ct) is not { } country) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        try
        {
            return Results.Ok(await registry.SearchAsync(new(country, request.Query, request.Filter, request.Page), ct));
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
    }

    /// <summary>Rows a ⌘K overlay shows: each one is checked against the care relationship, so the list stays short.</summary>
    const int LookupRows = 8;

    // shortcut: one clinical-access check per row (at most LookupRows); batch them if the p95 budget (300 ms) is ever at risk.
    static async Task<IResult> Lookup([FromBody] SearchRequest? request, [FromServices] IStaffAccess staff, [FromServices] ICurrentFacility facility,
        [FromServices] IClinicalAccess clinical, [FromServices] IPatientRegistry registry, CancellationToken ct)
    {
        if (await staff.TenantCountryAsync(ct) is not { } country || facility.FacilityId is not { } facilityId)
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        // Nothing typed lists nothing: this is a search box, not a directory.
        if (string.IsNullOrWhiteSpace(request?.Query)) return Results.Ok(new { items = Array.Empty<LookupRow>(), total = 0 });
        try
        {
            var found = await registry.SearchAsync(new(country, request.Query, DirectoryFilter.All, 1), ct);
            List<LookupRow> rows = [];
            foreach (var r in found.Items.Take(LookupRows))
                rows.Add(new(r.Id, r.Initials, r.AgeYears, r.AgeEstimated, r.MrnTail, await AccessOf(clinical, r.Id, facilityId, ct)));
            return Results.Ok(new { items = rows, total = found.Total });
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
    }

    static async Task<LookupAccess> AccessOf(IClinicalAccess clinical, Guid patientId, Guid facilityId, CancellationToken ct)
    {
        try
        {
            await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, [Access.Read, Access.Limited, Access.Full, Access.Sign], ct);
            return LookupAccess.Open;
        }
        catch (ClinicalAccessDeniedException e)
        {
            return !e.CanRequest ? LookupAccess.NoAccess : e.RequestPending ? LookupAccess.RequestPending : LookupAccess.CanRequest;
        }
    }

    static async Task<IResult> Get(Guid patientId, [FromServices] IPatientRegistry registry, [FromServices] TimeProvider clock, CancellationToken ct) =>
        await registry.GetAsync(patientId, ct) is { } r
            ? Results.Ok(new PatientDetails(
                r.Patient.Id, r.Patient.Mrn, r.Patient.FullName, r.Patient.ArabicName, r.Patient.DateOfBirth, r.Patient.DateOfBirthEstimated,
                CountryRules.AgeOn(CountryRules.For(r.Patient.CountryCode).Today(clock.GetUtcNow()), r.Patient.DateOfBirth), r.Patient.SexAtBirth, r.Patient.GenderIdentity, r.Patient.PreferredLanguage, r.Patient.Nationality, r.Patient.Mobile,
                r.Patient.AlternatePhone, r.Patient.Email, r.Patient.Address, r.Patient.Status,
                CountryRules.For(r.Patient.CountryCode).Today(r.Patient.RegisteredAt),
                [.. r.Identifiers.OrderBy(i => i.Scheme).Select(i => new IdentifierView(i.Scheme, LastFour(i.LookupValue ?? i.DisplayValue), i.ExpiresOn))],
                r.Payer is { } p ? new PayerView(p.Kind, p.PayerName, p.Plan, p.MemberNumber is null ? null : LastFour(p.MemberNumber), p.ValidTo, p.Network) : null,
                Contact(r.Relationships.FirstOrDefault(x => x.IsEmergencyContact)), Contact(r.Relationships.FirstOrDefault(x => x.IsGuardian))))
            : Results.NotFound();

    static async Task<IResult> Reveal(Guid patientId, [FromServices] IPatientRegistry registry, CancellationToken ct) =>
        await registry.GetAsync(patientId, ct) is { } r
            ? Results.Ok(new RevealedIdentifiers(
                [.. r.Identifiers.OrderBy(i => i.Scheme).Select(i => new RevealedIdentifier(i.Scheme, i.LookupValue ?? i.DisplayValue ?? ""))], r.Payer?.MemberNumber))
            : Results.NotFound();

    static ContactView? Contact(PatientRelationship? r) => r is null ? null : new(r.Name, r.Relationship, r.Phone);

    static string LastFour(string? value) => value is null ? "" : value.Length <= 4 ? value : value[^4..];

    /// <summary>400 with field/code pairs only (the UI translates the codes); never the submitted values.</summary>
    internal static IResult Invalid(PatientValidationException e) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request",
            extensions: new Dictionary<string, object?> { ["errors"] = e.Errors });
}
