using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>A doctor of the practice as the Doctors tab shows them. Fees are minor units of <see cref="Currency"/>. Id is the doctor's membership.</summary>
public sealed record DoctorView(
    Guid Id, string FullName, string? Specialty, string? Qualifications, string? Languages, Guid FacilityId, string FacilityName, MembershipStatus Status,
    string Authority, string Number, string? Category, DateOnly? ExpiresOn, LicenceStatus Licence, string? RejectionReason,
    bool HasSignature, long NewVisitFee, long FollowUpFee, string Currency);

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
    public string? Authority { get; init; }
    public string? LicenceNumber { get; init; }
    public string? Category { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public string? CertificateName { get; init; }
    public long? NewVisitFee { get; init; }
    public long? FollowUpFee { get; init; }
}

public sealed record ResubmitLicence
{
    public string? Authority { get; init; }
    public string? Number { get; init; }
    public string? Category { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public string? CertificateName { get; init; }
}

/// <summary>
/// Doctors of the current tenant, added in clinic mode (MDV-P1-007). The caller's capability is checked by the endpoints; tenant
/// filters and row-level security scope every row. Each change is audited. A new doctor can record and sign only once Medvya
/// operations verify the licence (<see cref="ILicenceReview"/>, <see cref="LicenceRules"/>).
/// </summary>
public interface IDoctors
{
    Task<DoctorList> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: the doctor's Invited membership at the facility, profile, licence (verification pending) and invitation, which is
    /// emailed. Null when the facility is not one of the tenant's.
    /// </summary>
    /// <exception cref="SettingsValidationException">Field/code pairs, never the entered values.</exception>
    Task<DoctorView?> AddAsync(AddDoctor command, CancellationToken cancellationToken = default);

    /// <summary>Corrects a pending or rejected licence and sends it for verification again. Null when there is no such doctor. A verified licence is refused ("licence:verified").</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<DoctorView?> ResubmitLicenceAsync(Guid doctorId, ResubmitLicence command, CancellationToken cancellationToken = default);

    /// <summary>Stores the signature image (PNG or JPEG, as for logos) privately. False when there is no such doctor.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<bool> SetSignatureAsync(Guid doctorId, byte[] content, CancellationToken cancellationToken = default);

    Task<bool> RemoveSignatureAsync(Guid doctorId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The back-office licence decision (MDV-P1-007). Not a practice capability: the endpoint is for the Medvya operations capability only.
/// Runs in the tenant of the licence. Records who and when, writes the audit event in the same transaction, and tells the practice why on rejection.
/// </summary>
public interface ILicenceReview
{
    /// <summary>False when the tenant has no such licence. A rejection needs a reason (<see cref="SettingsValidationException"/>).</summary>
    Task<bool> DecideAsync(Guid doctorId, bool verified, string? reason, CancellationToken cancellationToken = default);
}
