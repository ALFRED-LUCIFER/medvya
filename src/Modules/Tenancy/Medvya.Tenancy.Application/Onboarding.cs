using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

// The draft of the onboarding wizard (F-04). One section per step; a section is null until its step was filled in.
public sealed record OnboardingProfile(string? FullName, string? Mode, string? Specialty, string? Qualifications, string? Languages);
public sealed record OnboardingLicence(string? Authority, string? Number, string? CertificateName);
public sealed record OnboardingClinic(string? Name, string? Address, string? RegistrationNumber, string? TaxId);
public sealed record HoursWindow(string? Start, string? End);
public sealed record OnboardingHours(IReadOnlyList<HoursWindow>? Weekdays, IReadOnlyList<HoursWindow>? Saturday, int? SlotMinutes, long? NewVisitFee, long? FollowUpFee);
public sealed record OnboardingTeamMember(string? Email, Role? Role);
public sealed record OnboardingData(
    OnboardingProfile? Profile, OnboardingLicence? Licence, OnboardingClinic? Clinic, OnboardingHours? Hours,
    IReadOnlyList<string>? Templates, IReadOnlyList<OnboardingTeamMember>? Team);

/// <summary>One invalid field. Field is the path in <see cref="OnboardingData"/> (e.g. "licence.number"); Code a stable key the UI translates.</summary>
public sealed record OnboardingError(string Field, string Code);

public sealed record ProvisionedPractice(Guid TenantId, Guid FacilityId, Guid MembershipId, Role Role);

/// <summary>Creates a practice from a finished, validated draft (MDV-P1-004).</summary>
public interface IPracticeProvisioning
{
    /// <summary>
    /// In one transaction: tenant (bound to the country), facility, owner membership, practice profile, hours and fee defaults and,
    /// for a doctor, the submitted licence (verification pending). The creation is audited in the new tenant before it commits.
    /// The step-6 invitations are sent afterwards through <see cref="IInvitations"/>, inside the new tenant (the caller enters it).
    /// </summary>
    Task<ProvisionedPractice> CreateAsync(string country, OnboardingData data, CancellationToken cancellationToken = default);
}
