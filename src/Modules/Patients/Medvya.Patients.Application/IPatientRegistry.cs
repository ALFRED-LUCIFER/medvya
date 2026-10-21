using Medvya.Patients.Domain;

namespace Medvya.Patients.Application;

public sealed record IdentifierInput(IdentifierScheme Scheme, string Value, DateOnly? ExpiresOn = null);

public sealed record PayerInput(PayerKind Kind, string? PayerName = null, string? Plan = null, string? MemberNumber = null, DateOnly? ValidTo = null, string? Network = null);

/// <summary>
/// Full or quick registration (MDV-P1-013/014/015). Quick mode takes the minimum safe fields; without address and payer the
/// patient is saved as <see cref="RegistrationStatus.Incomplete"/>.
/// </summary>
public sealed record RegisterPatient
{
    public required Guid FacilityId { get; init; }
    /// <summary>Tenant country, "IN" or "AE"; selects the country rules.</summary>
    public required string CountryCode { get; init; }
    public bool Quick { get; init; }
    public required string FullName { get; init; }
    public string? ArabicName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    /// <summary>India only: stated age when the date of birth is unknown; the date is then marked estimated.</summary>
    public int? AgeYears { get; init; }
    public required SexAtBirth SexAtBirth { get; init; }
    public string? GenderIdentity { get; init; }
    public string? PreferredLanguage { get; init; }
    public string? Nationality { get; init; }
    public required string Mobile { get; init; }
    public string? AlternatePhone { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<IdentifierInput> Identifiers { get; init; } = [];
    public PatientAddress? Address { get; init; }
    public PayerInput? Payer { get; init; }
}

/// <summary>What the success screen shows: MRN, time and actor. Warnings are codes (e.g. "emirates_id.expired") that never block.</summary>
public sealed record RegistrationResult(Guid PatientId, string Mrn, DateTimeOffset RegisteredAt, Guid? RegisteredBy, RegistrationStatus Status, IReadOnlyList<string> Warnings);

public sealed record PatientRecord(Patient Patient, IReadOnlyList<PatientIdentifier> Identifiers, PayerCoverage? Payer);

/// <summary>
/// Patients of the current tenant. Every call is audited (fail closed) and scoped by tenant query filters and row-level security.
/// shortcut: no HTTP endpoints yet; they need staff sign-in and capability policies (MDV-P0-011) and the P-02 screen.
/// </summary>
public interface IPatientRegistry
{
    /// <exception cref="PatientValidationException">With field/code pairs only; never the entered values.</exception>
    Task<RegistrationResult> RegisterAsync(RegisterPatient command, CancellationToken cancellationToken = default);

    Task<PatientRecord?> GetAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>Exact match on a lookup identifier (ABHA number, ABHA address, Emirates ID), normalised by the country rules first.</summary>
    Task<PatientRecord?> FindByIdentifierAsync(string countryCode, IdentifierScheme scheme, string value, CancellationToken cancellationToken = default);

    /// <summary>Adds the missing address and payer; the registration becomes Complete (closing the desk's completion task) once both exist.</summary>
    Task<RegistrationStatus> CompleteRegistrationAsync(Guid patientId, PatientAddress? address, PayerInput? payer, CancellationToken cancellationToken = default);
}
