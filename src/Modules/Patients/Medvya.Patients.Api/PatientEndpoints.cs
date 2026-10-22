using Medvya.Audit.Api;
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

namespace Medvya.Patients.Api;

/// <summary>Search text travels in the POST body: names, phones and IDs are PHI and never go in a URL or query string.</summary>
public sealed record SearchRequest(string? Query = null, DirectoryFilter Filter = DirectoryFilter.All, int Page = 1);

public sealed record IdentifierView(IdentifierScheme Scheme, string Value, DateOnly? ExpiresOn);

public sealed record PayerView(PayerKind Kind, string? PayerName, string? Plan, string? MemberNumber, DateOnly? ValidTo, string? Network);

/// <summary>An opened record for the front desk: registration data only, never clinical data.</summary>
public sealed record PatientDetails(
    Guid Id, string Mrn, string FullName, string? ArabicName, DateOnly DateOfBirth, bool DateOfBirthEstimated, int AgeYears, SexAtBirth SexAtBirth,
    string? GenderIdentity, string? PreferredLanguage, string? Nationality, string Mobile, string? AlternatePhone, string? Email,
    PatientAddress? Address, RegistrationStatus Status, DateTimeOffset RegisteredAt, IReadOnlyList<IdentifierView> Identifiers, PayerView? Payer);

/// <summary>What the desk has typed so far; identifiers that are not complete yet are skipped, not rejected.</summary>
public sealed record DuplicateCheckRequest(string? FullName = null, DateOnly? DateOfBirth = null, IReadOnlyList<IdentifierInput>? Identifiers = null);

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
    public DuplicateResolution? Duplicates { get; init; }

    internal RegisterPatient ToCommand(Guid facilityId, string countryCode) => new()
    {
        FacilityId = facilityId, CountryCode = countryCode, Quick = Quick, FullName = FullName, ArabicName = ArabicName,
        DateOfBirth = DateOfBirth, AgeYears = AgeYears, SexAtBirth = SexAtBirth, GenderIdentity = GenderIdentity,
        PreferredLanguage = PreferredLanguage, Nationality = Nationality, Mobile = Mobile, AlternatePhone = AlternatePhone, Email = Email,
        Identifiers = Identifiers, Address = Address, Payer = Payer, Duplicates = Duplicates,
    };
}

public static class PatientEndpoints
{
    /// <summary>Rate-limit policy for search endpoints (registered by the host, per signed-in user).</summary>
    public const string SearchRateLimit = "patient-search";
    /// <summary>Rate-limit policy for the duplicate check, which runs while the desk types (registered by the host, per user).</summary>
    public const string DuplicateCheckRateLimit = "patient-duplicate-check";
    const string Purpose = "registration";

    /// <summary>
    /// Front-desk patient endpoints (MDV-P1-018, MDV-P1-016). Reading needs Register / book patients at Read or Full, registering
    /// at Full. Reads are audited before they run; a registration audits inside its own transaction.
    /// </summary>
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var patients = app.MapGroup("/v1/patients").RequireCapability(Capability.RegisterBookPatients, Access.Read, Access.Full);

        patients.MapPost("/search", Search)
            .Audited("patient.search", "patient", Purpose)
            .RequireRateLimiting(SearchRateLimit);

        patients.MapGet("/{patientId:guid}", Get)
            .Audited("patient.read", "patient", Purpose, patientRouteKey: "patientId");

        patients.MapPost("/duplicate-check", DuplicateCheck)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .Audited("patient.duplicate_check", "patient", Purpose)
            .RequireRateLimiting(DuplicateCheckRateLimit);

        patients.MapPost("/", Register)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .AuditedByUseCase("patient.register", "patient", Purpose);
        return app;
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

    static async Task<IResult> Register([FromBody] RegisterRequest request, [FromServices] IStaffAccess staff, [FromServices] ICurrentFacility facility,
        [FromServices] IPatientRegistry registry, CancellationToken ct)
    {
        if (await staff.TenantCountryAsync(ct) is not { } country || facility.FacilityId is not { } facilityId)
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "No facility context");
        try
        {
            // Facility and country come from the session and the tenant, never from the request body.
            return Results.Ok(await registry.RegisterAsync(request.ToCommand(facilityId, country), ct));
        }
        catch (PatientValidationException e)
        {
            return Invalid(e);
        }
    }

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

    static async Task<IResult> Get(Guid patientId, [FromServices] IPatientRegistry registry, [FromServices] TimeProvider clock, CancellationToken ct) =>
        await registry.GetAsync(patientId, ct) is { } r
            ? Results.Ok(new PatientDetails(
                r.Patient.Id, r.Patient.Mrn, r.Patient.FullName, r.Patient.ArabicName, r.Patient.DateOfBirth, r.Patient.DateOfBirthEstimated,
                CountryRules.AgeOn(CountryRules.For(r.Patient.CountryCode).Today(clock.GetUtcNow()), r.Patient.DateOfBirth), r.Patient.SexAtBirth, r.Patient.GenderIdentity, r.Patient.PreferredLanguage, r.Patient.Nationality, r.Patient.Mobile,
                r.Patient.AlternatePhone, r.Patient.Email, r.Patient.Address, r.Patient.Status, r.Patient.RegisteredAt,
                [.. r.Identifiers.OrderBy(i => i.Scheme).Select(i => new IdentifierView(i.Scheme, i.LookupValue ?? i.DisplayValue ?? "", i.ExpiresOn))],
                r.Payer is { } p ? new PayerView(p.Kind, p.PayerName, p.Plan, p.MemberNumber, p.ValidTo, p.Network) : null))
            : Results.NotFound();

    /// <summary>400 with field/code pairs only (the UI translates the codes); never the submitted values.</summary>
    internal static IResult Invalid(PatientValidationException e) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request",
            extensions: new Dictionary<string, object?> { ["errors"] = e.Errors });
}
