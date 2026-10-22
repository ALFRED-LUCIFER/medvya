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

public sealed record IdentifierView(IdentifierScheme Scheme, string Value, DateOnly? ExpiresOn);

public sealed record PayerView(PayerKind Kind, string? PayerName, string? Plan, string? MemberNumber, DateOnly? ValidTo, string? Network);

/// <summary>An opened record for the front desk: registration data only, never clinical data.</summary>
public sealed record PatientDetails(
    Guid Id, string Mrn, string FullName, string? ArabicName, DateOnly DateOfBirth, bool DateOfBirthEstimated, int AgeYears, SexAtBirth SexAtBirth,
    string? GenderIdentity, string? PreferredLanguage, string? Nationality, string Mobile, string? AlternatePhone, string? Email,
    PatientAddress? Address, RegistrationStatus Status, DateTimeOffset RegisteredAt, IReadOnlyList<IdentifierView> Identifiers, PayerView? Payer);

public static class PatientEndpoints
{
    /// <summary>Rate-limit policy for search endpoints (registered by the host, per signed-in user).</summary>
    public const string SearchRateLimit = "patient-search";
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

        patients.MapGet("/{patientId:guid}", Get)
            .Audited("patient.read", "patient", Purpose, patientRouteKey: "patientId");
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
                r.Patient.AlternatePhone, r.Patient.Email, r.Patient.Address, r.Patient.Status, r.Patient.RegisteredAt,
                [.. r.Identifiers.OrderBy(i => i.Scheme).Select(i => new IdentifierView(i.Scheme, i.LookupValue ?? i.DisplayValue ?? "", i.ExpiresOn))],
                r.Payer is { } p ? new PayerView(p.Kind, p.PayerName, p.Plan, p.MemberNumber, p.ValidTo, p.Network) : null))
            : Results.NotFound();

    /// <summary>400 with field/code pairs only (the UI translates the codes); never the submitted values.</summary>
    internal static IResult Invalid(PatientValidationException e) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request",
            extensions: new Dictionary<string, object?> { ["errors"] = e.Errors });
}
