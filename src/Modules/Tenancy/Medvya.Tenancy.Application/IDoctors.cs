using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>A doctor of the practice as the Doctors tab shows them. Fees are minor units of <see cref="Currency"/>. Id is the doctor's membership.</summary>
public sealed record DoctorView(
    Guid Id, string FullName, string? Specialty, string? Qualifications, string? Languages, Guid FacilityId, string FacilityName, MembershipStatus Status,
    string Authority, string Number, string? Category, DateOnly? ExpiresOn, bool Expired, Guid? AttestedBy, string? AttestedByName, DateTimeOffset AttestedAt,
    bool HasSignature, long NewVisitFee, long FollowUpFee, string Currency, string? Room = null,
    // True for the signed-in doctor's own row: only they set or replace their signature image.
    bool OwnSignature = false);

public sealed record FeeDefaults(long NewVisitFee, long FollowUpFee, string Currency);

public sealed record DoctorList(IReadOnlyList<DoctorView> Doctors, FeeDefaults Defaults, IReadOnlyList<LicenceAuthority> Authorities);

public sealed record AddDoctor
{
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? Specialty { get; init; }
    public string? Qualifications { get; init; }
    public string? Languages { get; init; }
    public Guid FacilityId { get; init; }
    /// <summary>Other locations the doctor also works at (MDV-P1-071); each gets its own schedule, profile and fees.</summary>
    public IReadOnlyList<Guid>? AlsoFacilityIds { get; init; }
    public string? Authority { get; init; }
    public string? LicenceNumber { get; init; }
    public string? Category { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public string? CertificateName { get; init; }
    /// <summary>The owner confirms the licence entered is the doctor's own and takes responsibility for it (ADR-016). Required.</summary>
    public bool Attested { get; init; }
    public long? NewVisitFee { get; init; }
    public long? FollowUpFee { get; init; }
    /// <summary>The doctor's room at <see cref="FacilityId"/> (MDV-P1-111); the other locations' rooms are set from the doctors list.</summary>
    public string? Room { get; init; }
}

public sealed record DoctorRoom(string? Room);

public sealed record ResubmitLicence
{
    public bool Attested { get; init; }
    public string? Authority { get; init; }
    public string? Number { get; init; }
    public string? Category { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public string? CertificateName { get; init; }
}

/// <summary>
/// Doctors of the current tenant, added in clinic mode (MDV-P1-007). The caller's capability is checked by the endpoints; tenant
/// filters and row-level security scope every row. Each change is audited. The owner attests each licence; Medvya does not check it
/// and no capability waits for it (ADR-016).
/// </summary>
public interface IDoctors
{
    Task<DoctorList> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: the doctor's Invited membership at the facility, profile, licence (attested by the owner) and invitation, which is
    /// emailed. Null when the facility is not one of the tenant's.
    /// </summary>
    /// <exception cref="SettingsValidationException">Field/code pairs, never the entered values.</exception>
    Task<DoctorView?> AddAsync(AddDoctor command, CancellationToken cancellationToken = default);

    /// <summary>Replaces the licence record (for example a renewal) and records the owner's attestation of it. Null when there is no such doctor.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<DoctorView?> ResubmitLicenceAsync(Guid doctorId, ResubmitLicence command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the signature image (PNG or JPEG, as for logos) privately. Only the doctor sets their own: false for anyone else, and for a
    /// doctor who has not joined yet. Null when there is no such doctor.
    /// </summary>
    /// <exception cref="SettingsValidationException"/>
    Task<bool?> SetSignatureAsync(Guid doctorId, byte[] content, CancellationToken cancellationToken = default);

    Task<bool> RemoveSignatureAsync(Guid doctorId, CancellationToken cancellationToken = default);

    /// <summary>The signed-in doctor's own profile at the location they work in now, and whether it has a signature image. Null when the caller is not a doctor here.</summary>
    Task<OwnSignature?> OwnSignatureAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets (or, blank, clears) the doctor's room at the doctor's location, audited. Null when there is no such doctor.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<DoctorView?> SetRoomAsync(Guid doctorId, string? room, CancellationToken cancellationToken = default);
}

public sealed record OwnSignature(Guid DoctorId, bool HasSignature);
