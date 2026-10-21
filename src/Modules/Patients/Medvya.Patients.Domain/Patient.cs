using Medvya.BuildingBlocks.Domain;

namespace Medvya.Patients.Domain;

public enum SexAtBirth { Female, Male, Other }

/// <summary>Incomplete = quick-registered (or saved without address or payer): the desk shows "Incomplete registration" until it is completed.</summary>
public enum RegistrationStatus { Complete, Incomplete }

/// <summary>
/// A registered patient of one facility (MDV-P1-013/014/015). Demographics only: identifiers and payer live in their own
/// tables so the Always Encrypted column set stays small (docs/03-architecture/data-classification.md).
/// </summary>
public sealed class Patient
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>Facility-scoped medical record number, assigned on registration.</summary>
    [Classification(DataClass.Internal)] public required string Mrn { get; init; }
    /// <summary>Country of the tenant at registration; selects the country rules.</summary>
    [Classification(DataClass.Internal)] public required string CountryCode { get; init; }
    /// <summary>Full name as on the ID document (UAE: as on the Emirates ID, in English).</summary>
    [Classification(DataClass.Personal)] public required string FullName { get; set; }
    [Classification(DataClass.Personal)] public string? ArabicName { get; set; }
    [Classification(DataClass.Personal)] public DateOnly DateOfBirth { get; set; }
    /// <summary>True when the date of birth was derived from a stated age (India).</summary>
    [Classification(DataClass.Personal)] public bool DateOfBirthEstimated { get; set; }
    [Classification(DataClass.SensitiveHealth)] public SexAtBirth SexAtBirth { get; set; }
    [Classification(DataClass.SensitiveHealth)] public string? GenderIdentity { get; set; }
    [Classification(DataClass.Personal)] public string? PreferredLanguage { get; set; }
    /// <summary>ISO 3166-1 alpha-2.</summary>
    [Classification(DataClass.Personal)] public string? Nationality { get; set; }
    /// <summary>E.164, e.g. +9198…; normalised by the country rules.</summary>
    [Classification(DataClass.Personal)] public required string Mobile { get; set; }
    [Classification(DataClass.Personal)] public string? AlternatePhone { get; set; }
    [Classification(DataClass.Personal)] public string? Email { get; set; }
    [Classification(DataClass.Personal)] public PatientAddress? Address { get; set; }
    [Classification(DataClass.Internal)] public RegistrationStatus Status { get; set; }
    [Classification(DataClass.Internal)] public Guid? RegisteredBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RegisteredAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// One address shape for both countries. India: Line1 house/flat, Line2 street, City, District, Region state/UT, PostalCode PIN.
/// UAE: Line1 villa/building, Line2 area, City, Region emirate code, Makani, PoBox.
/// </summary>
public sealed record PatientAddress
{
    [Classification(DataClass.Personal)] public required string Line1 { get; init; }
    [Classification(DataClass.Personal)] public string? Line2 { get; init; }
    [Classification(DataClass.Personal)] public string? City { get; init; }
    [Classification(DataClass.Personal)] public string? District { get; init; }
    [Classification(DataClass.Personal)] public string? Region { get; init; }
    [Classification(DataClass.Personal)] public string? PostalCode { get; init; }
    [Classification(DataClass.Personal)] public string? Makani { get; init; }
    [Classification(DataClass.Personal)] public string? PoBox { get; init; }
}

public enum IdentifierScheme { Abha, AbhaAddress, AadhaarLast4, EmiratesId, Passport, Visa }

/// <summary>
/// A national, health or travel identifier. Always Encrypted (MDV-P0-009): schemes matched exactly (ABHA number and address,
/// Emirates ID, and passport for the duplicate check, MDV-P1-016) go in the deterministic LookupValue, the rest (Aadhaar last 4,
/// visa file) in the randomized DisplayValue. Exactly one is set.
/// </summary>
public sealed class PatientIdentifier
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public IdentifierScheme Scheme { get; init; }
    [Classification(DataClass.NationalId)] public string? LookupValue { get; init; }
    [Classification(DataClass.NationalId)] public string? DisplayValue { get; init; }
    /// <summary>Document expiry (Emirates ID, passport, visa).</summary>
    [Classification(DataClass.Personal)] public DateOnly? ExpiresOn { get; init; }

    public static bool IsLookup(IdentifierScheme scheme) =>
        scheme is IdentifierScheme.Abha or IdentifierScheme.AbhaAddress or IdentifierScheme.EmiratesId or IdentifierScheme.Passport;

    /// <summary>Schemes the desk search box recognises (MDV-P1-018). A passport number looks like any short code, so it is matched by the duplicate check only.</summary>
    public static bool IsDeskSearchKey(IdentifierScheme scheme) => scheme is IdentifierScheme.Abha or IdentifierScheme.AbhaAddress or IdentifierScheme.EmiratesId;
}

/// <summary>Proposed: the desk asked for a merge review (P-03, MDV-P1-017). NotSame: the desk decided the two are different people.</summary>
public enum MergeRequestStatus { Proposed, NotSame }

/// <summary>
/// A duplicate decision taken at registration (MDV-P1-016) about a new patient and an existing possible match. Records only the
/// decision; merging and the second approver are MDV-P1-017 (state machine: docs/03-architecture/state-machines.md).
/// </summary>
public sealed class MergeRequest
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>The record just registered.</summary>
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>The existing record it was matched with.</summary>
    [Classification(DataClass.Internal)] public Guid CandidatePatientId { get; init; }
    [Classification(DataClass.Internal)] public MergeRequestStatus Status { get; init; }
    [Classification(DataClass.Internal)] public Guid? DecidedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset DecidedAt { get; init; }
}

/// <summary>India: Self-pay, Corporate, Private insurance (TPA), Government scheme. UAE: Insurance (= PrivateInsurance), Self-pay, Corporate.</summary>
public enum PayerKind { SelfPay, Corporate, PrivateInsurance, GovernmentScheme }

public sealed class PayerCoverage
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Internal)] public PayerKind Kind { get; init; }
    /// <summary>Insurer, TPA, corporate or government scheme name (reference only in P1).</summary>
    [Classification(DataClass.Internal)] public string? PayerName { get; init; }
    [Classification(DataClass.Internal)] public string? Plan { get; init; }
    /// <summary>Insurance member card / policy number. Always Encrypted, deterministic (exact match for eligibility and claims).</summary>
    [Classification(DataClass.NationalId)] public string? MemberNumber { get; init; }
    [Classification(DataClass.Internal)] public DateOnly? ValidTo { get; init; }
    [Classification(DataClass.Internal)] public string? Network { get; init; }
}
