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
    /// <summary>A code from the registration form (e.g. "NonBinary"), shown translated; optional and separate from sex at birth.</summary>
    [Classification(DataClass.SensitiveHealth)] public string? GenderIdentity { get; set; }
    /// <summary>BCP 47 language code (e.g. "ml"), named in the UI language.</summary>
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
    /// <summary>Set when a merge (MDV-P1-017) mapped this record to its survivor; the record is kept, hidden from search, and cleared by an undo.</summary>
    [Classification(DataClass.Internal)] public Guid? MergedIntoPatientId { get; set; }
}

/// <summary>
/// One address shape for both countries. India: Line1 house/flat, Line2 street, City, District, Region state/UT, PostalCode PIN.
/// India Region is the state/UT's official English name (IndiaRules.StatesAndUnionTerritories).
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
    [Classification(DataClass.Internal)] public Guid PatientId { get; set; }
    [Classification(DataClass.Internal)] public IdentifierScheme Scheme { get; init; }
    [Classification(DataClass.NationalId)] public string? LookupValue { get; init; }
    [Classification(DataClass.NationalId)] public string? DisplayValue { get; init; }
    /// <summary>Document expiry (Emirates ID, passport, visa).</summary>
    [Classification(DataClass.Personal)] public DateOnly? ExpiresOn { get; init; }
    /// <summary>Set while a merge (MDV-P1-017) holds this identifier on the survivor: the record it came from, so an undo moves it back.</summary>
    [Classification(DataClass.Internal)] public Guid? MovedFromPatientId { get; set; }

    public static bool IsLookup(IdentifierScheme scheme) =>
        scheme is IdentifierScheme.Abha or IdentifierScheme.AbhaAddress or IdentifierScheme.EmiratesId or IdentifierScheme.Passport;

    /// <summary>Schemes the desk search box recognises (MDV-P1-018). A passport number looks like any short code, so it is matched by the duplicate check only.</summary>
    public static bool IsDeskSearchKey(IdentifierScheme scheme) => scheme is IdentifierScheme.Abha or IdentifierScheme.AbhaAddress or IdentifierScheme.EmiratesId;
}

/// <summary>How a contact is related to the patient. Country-specific: Sponsor (of a dependant) is used in the UAE only.</summary>
public enum RelationshipKind { Spouse, Parent, Child, Sibling, Friend, Sponsor, Other }

/// <summary>
/// An emergency contact or guardian of a patient (MDV-P1-020, PATIENT_RELATIONSHIP). A name to call, never a user: no account,
/// portal access or patient data is created for them (caregiver access is a patient-authorised proxy, P4 R-04).
/// </summary>
public sealed class PatientRelationship
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    [Classification(DataClass.Personal)] public required string Name { get; init; }
    [Classification(DataClass.Personal)] public RelationshipKind Relationship { get; init; }
    /// <summary>E.164, normalised by the country rules like the patient's mobile.</summary>
    [Classification(DataClass.Personal)] public required string Phone { get; init; }
    [Classification(DataClass.Internal)] public bool IsEmergencyContact { get; init; }
    /// <summary>Required while the patient is under 18 (UAE: also the sponsor of a dependant).</summary>
    [Classification(DataClass.Internal)] public bool IsGuardian { get; init; }
}

/// <summary>
/// State machine: docs/03-architecture/state-machines.md. Proposed (a review was asked for) goes to NotSame (closed, the pair is
/// suppressed) or AwaitingApproval (clinician approval asked), then Merged (reversible) and Unmerged.
/// </summary>
public enum MergeRequestStatus { Proposed, NotSame, AwaitingApproval, Merged, Unmerged }

/// <summary>
/// A merge review about two records (MDV-P1-016 creates it at registration, MDV-P1-017 decides it). The survivor is the older record
/// (<see cref="CandidatePatientId"/>); <see cref="PatientId"/> is mapped to it. Nothing is deleted, so a merge can be undone.
/// </summary>
public sealed class MergeRequest
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>The record that is mapped to the survivor when merged (at registration: the record just registered).</summary>
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>The surviving record (at registration: the existing record it was matched with).</summary>
    [Classification(DataClass.Internal)] public Guid CandidatePatientId { get; init; }
    [Classification(DataClass.Internal)] public MergeRequestStatus Status { get; set; }
    /// <summary>Who took the latest non-approval decision (asked for the review, or said not the same, or asked for approval).</summary>
    [Classification(DataClass.Internal)] public Guid? DecidedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset DecidedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? FirstApprovedBy { get; set; }
    /// <summary>Required, and a different clinician, when both records hold clinical data.</summary>
    [Classification(DataClass.Internal)] public Guid? SecondApprovedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? MergedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? UnmergedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? UnmergedAt { get; set; }
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
    /// <summary>UAE: the provider network or tier. India: the TPA that administers the policy (the claims network).</summary>
    [Classification(DataClass.Internal)] public string? Network { get; init; }
}
