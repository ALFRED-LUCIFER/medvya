using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>
/// What onboarding collects beyond the practice profile and opening hours of settings (MDV-P1-005): the doctor's own profile,
/// appointment length, fee defaults and note templates. One per tenant. Fees are integer minor units in the tenant's currency
/// (AGENTS.md 3.4). The booking windows keep the morning and evening split that the one-row-per-day OperatingHours cannot hold,
/// to seed schedule templates later.
/// </summary>
public sealed class PracticeDefaults
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Public)] public string? Specialty { get; set; }
    [Classification(DataClass.Public)] public string? Qualifications { get; set; }
    [Classification(DataClass.Public)] public string? Languages { get; set; }
    [Classification(DataClass.Internal)] public int SlotMinutes { get; set; }
    [Classification(DataClass.Internal)] public long NewVisitFeeMinor { get; set; }
    [Classification(DataClass.Internal)] public long FollowUpFeeMinor { get; set; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    /// <summary>JSON lists of "HH:mm" start/end windows.</summary>
    [Classification(DataClass.Internal)] public required string WeekdayHours { get; set; }
    [Classification(DataClass.Internal)] public required string SaturdayHours { get; set; }
    /// <summary>JSON list of note template ids (used from P2/P3).</summary>
    [Classification(DataClass.Internal)] public required string NoteTemplates { get; set; }
    /// <summary>How the practice chose to bring its patients in set-up: "Upload", "Move" or "Fresh"; null when skipped (MDV-P1-061).</summary>
    // shortcut: only the choice is kept; there is no spreadsheet import or migration request queue yet. Build them with the patient import ticket.
    [Classification(DataClass.Internal)] public string? PatientStart { get; set; }
}

/// <summary>
/// A doctor's licence as entered and attested by the practice owner (ADR-016, MDV-P1-065). Medvya does not check it and no capability
/// depends on it; the owner answers for it. The values attested are kept in the append-only settings change row.
/// </summary>
public sealed class DoctorLicence
{
    [Classification(DataClass.Internal)] public Guid MembershipId { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public required string Authority { get; set; }
    [Classification(DataClass.Personal)] public required string Number { get; set; }
    /// <summary>Registration category (e.g. permanent, provisional); null for licences submitted in the sign-up wizard, which does not ask.</summary>
    [Classification(DataClass.Internal)] public string? Category { get; set; }
    [Classification(DataClass.Internal)] public DateOnly? ExpiresOn { get; set; }
    /// <summary>Name of the certificate file the doctor chose; the file itself is not stored yet.</summary>
    [Classification(DataClass.Personal)] public string? CertificateName { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset SubmittedAt { get; set; }
    /// <summary>The owner who attested the licence (null only for a licence whose tenant had no owner at migration, MDV-P1-068).</summary>
    [Classification(DataClass.Internal)] public Guid? AttestedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset AttestedAt { get; set; }
}

/// <summary>
/// A doctor added in clinic mode (MDV-P1-007), one per doctor membership: the profile shown on the doctor list, fee defaults and the
/// signature image. Fees are integer minor units in the tenant's currency (AGENTS.md 3.4).
/// </summary>
public sealed class DoctorProfile
{
    [Classification(DataClass.Internal)] public Guid MembershipId { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Personal)] public required string FullName { get; set; }
    [Classification(DataClass.Public)] public string? Specialty { get; set; }
    [Classification(DataClass.Public)] public string? Qualifications { get; set; }
    [Classification(DataClass.Public)] public string? Languages { get; set; }
    [Classification(DataClass.Internal)] public long NewVisitFeeMinor { get; set; }
    [Classification(DataClass.Internal)] public long FollowUpFeeMinor { get; set; }
    /// <summary>Private Blob key of the signature image. Printed on prescriptions only; never served to the screen, never a way to sign.</summary>
    [Classification(DataClass.Personal)] public string? SignatureKey { get; set; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}

public static class LicenceRules
{
    /// <summary>A licence is valid through its expiry date (inclusive) and expired from the day after (MDV-P1-010). No date means no expiry.</summary>
    public static bool IsExpired(DoctorLicence l, DateOnly today) => l.ExpiresOn is { } expiry && expiry < today;
}

/// <summary>What the owner decided about one person at a quarterly access review (MDV-P1-010).</summary>
public enum AccessDecision { Confirmed, Offboarded }

/// <summary>
/// ACCESS_REVIEW (MDV-P1-010): one per tenant and calendar quarter ("2026-Q4"), opened when the owner first looks at it. It is complete
/// when every active member has a decision; the completion time is what Settings shows.
/// </summary>
public sealed class AccessReview
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public required string Quarter { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset StartedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>One decision about one membership in one review; the first one stands.</summary>
public sealed class AccessReviewDecision
{
    [Classification(DataClass.Internal)] public Guid ReviewId { get; init; }
    [Classification(DataClass.Internal)] public Guid MembershipId { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public AccessDecision Decision { get; init; }
    [Classification(DataClass.Internal)] public Guid? DecidedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset DecidedAt { get; init; }
}

/// <summary>Ids only: lets the background job find whose scope to enter without reading any tenant's rows (filled by a trigger on new tenants).</summary>
public sealed class TenantIndexEntry
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
}
