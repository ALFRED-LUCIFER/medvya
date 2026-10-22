using Medvya.Audit.Api;
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
    PatientAddress? Address, RegistrationStatus Status, DateOnly RegisteredOn, IReadOnlyList<IdentifierView> Identifiers, PayerView? Payer);

/// <summary>The full numbers, returned only by the audited reveal (checking the patient's card or a claim at the desk).</summary>
public sealed record RevealedIdentifiers(IReadOnlyList<RevealedIdentifier> Identifiers, string? MemberNumber);

public sealed record RevealedIdentifier(IdentifierScheme Scheme, string Value);

public static class PatientEndpoints
{
    /// <summary>Rate-limit policy for search endpoints (registered by the host, per signed-in user).</summary>
    public const string SearchRateLimit = "patient-search";
    /// <summary>Rate-limit policy for opening records and revealing their numbers (registered by the host, per signed-in user).</summary>
    public const string RecordRateLimit = "patient-record";
    const string Purpose = "registration";

    /// <summary>
    /// Front-desk patient endpoints (MDV-P1-018). Register / book patients at Read or Full; every request is audited before it runs.
    /// </summary>
    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var patients = app.MapGroup("/v1/patients").RequireCapability(Capability.RegisterBookPatients, Access.Read, Access.Full);

        patients.MapPost("/search", Search)
            .Audited("patient.search", "patient", Purpose)
            .RequireRateLimiting(SearchRateLimit);

        // shortcut: per-user limits only; no alert on unusual read volume (e.g. one user opening hundreds of records a day) until the
        // practice access log exists (MDV-P1-035).
        patients.MapGet("/{patientId:guid}", Get)
            .Audited("patient.read", "patient", Purpose, patientRouteKey: "patientId")
            .RequireRateLimiting(RecordRateLimit);

        // POST, not GET: a reveal is an action that is audited on its own, never prefetched or cached.
        patients.MapPost("/{patientId:guid}/identifiers/reveal", Reveal)
            .RequireCapability(Capability.RegisterBookPatients, Access.Full)
            .Audited("patient.identifiers.reveal", "patient", Purpose, patientRouteKey: "patientId")
            .RequireRateLimiting(RecordRateLimit);
        return app;
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
                r.Patient.AlternatePhone, r.Patient.Email, r.Patient.Address, r.Patient.Status,
                CountryRules.For(r.Patient.CountryCode).Today(r.Patient.RegisteredAt),
                [.. r.Identifiers.OrderBy(i => i.Scheme).Select(i => new IdentifierView(i.Scheme, LastFour(i.LookupValue ?? i.DisplayValue), i.ExpiresOn))],
                r.Payer is { } p ? new PayerView(p.Kind, p.PayerName, p.Plan, p.MemberNumber is null ? null : LastFour(p.MemberNumber), p.ValidTo, p.Network) : null))
            : Results.NotFound();

    static async Task<IResult> Reveal(Guid patientId, [FromServices] IPatientRegistry registry, CancellationToken ct) =>
        await registry.GetAsync(patientId, ct) is { } r
            ? Results.Ok(new RevealedIdentifiers(
                [.. r.Identifiers.OrderBy(i => i.Scheme).Select(i => new RevealedIdentifier(i.Scheme, i.LookupValue ?? i.DisplayValue ?? ""))], r.Payer?.MemberNumber))
            : Results.NotFound();

    static string LastFour(string? value) => value is null ? "" : value.Length <= 4 ? value : value[^4..];

    /// <summary>400 with field/code pairs only (the UI translates the codes); never the submitted values.</summary>
    internal static IResult Invalid(PatientValidationException e) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request",
            extensions: new Dictionary<string, object?> { ["errors"] = e.Errors });
}
