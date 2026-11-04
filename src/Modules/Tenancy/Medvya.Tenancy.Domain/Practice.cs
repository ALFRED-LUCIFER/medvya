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
/// A doctor's licence as submitted. Until <see cref="VerifiedAt"/> is set (back-office check, MDV-P1-007) the doctor can book and
/// register patients but not record or sign (<see cref="LicenceRules"/>).
/// </summary>
public sealed class DoctorLicence
{
    [Classification(DataClass.Internal)] public Guid MembershipId { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public required string Authority { get; init; }
    [Classification(DataClass.Personal)] public required string Number { get; init; }
    /// <summary>Name of the certificate file the doctor chose; the file itself is not stored yet.</summary>
    [Classification(DataClass.Personal)] public string? CertificateName { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SubmittedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? VerifiedAt { get; set; }
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
}
