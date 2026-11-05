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
}

/// <summary>
/// A doctor's licence as submitted. Until <see cref="VerifiedAt"/> is set (back-office decision, MDV-P1-007) the doctor can book and
/// register patients but not record or sign (<see cref="LicenceRules"/>). A rejected licence stays gated until it is corrected and verified.
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
    [Classification(DataClass.Internal)] public DateTimeOffset? VerifiedAt { get; set; }
    /// <summary>The Medvya operations user who verified the licence.</summary>
    [Classification(DataClass.Internal)] public Guid? VerifiedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RejectedAt { get; set; }
    /// <summary>Why operations rejected it; shown to the practice. Must not contain personal details (the screen says so).</summary>
    [Classification(DataClass.Internal)] public string? RejectionReason { get; set; }
}

public enum LicenceStatus { Pending, Verified, Rejected }

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
    /// <summary>Recording and signing capabilities that stay off until the licence is verified (F-04 notice).</summary>
    public static readonly IReadOnlySet<Capability> Gated = new HashSet<Capability>
    {
        Capability.RecordingConsentCapture, Capability.AiDraftsEvidence, Capability.ClinicalNotes,
        Capability.PrescriptionsOrders, Capability.ResultReviewAcknowledge,
    };

    /// <summary>The access the role's matrix grants, or None for a gated capability while the licence is unverified.</summary>
    public static Access Apply(Access access, Capability capability, bool licencePending) =>
        licencePending && Gated.Contains(capability) ? Access.None : access;

    public static LicenceStatus StatusOf(DoctorLicence l) =>
        l.VerifiedAt is not null ? LicenceStatus.Verified : l.RejectedAt is not null ? LicenceStatus.Rejected : LicenceStatus.Pending;
}
