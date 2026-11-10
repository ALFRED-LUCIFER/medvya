using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

// The draft of the set-up wizard (F-04, 6 Nov design, MDV-P1-061). A section is null until its step was filled in.
// Specialties are codes from OnboardingRules.Specialties; Specialty is the free text drafts saved before them.
public sealed record OnboardingProfile(string? FullName, string? Mode, string? Specialty, string? Qualifications, string? Languages, IReadOnlyList<string>? Specialties = null);
public sealed record OnboardingLicence(string? Authority, string? Number, string? CertificateName, bool Attested = false);
// Type: "Clinic" or "Hospital", chosen first (ADR-016, MDV-P1-062). ContactEmail is where Medvya writes about the request; it becomes the practice contact email.
public sealed record OnboardingClinic(string? Name, string? Address, string? RegistrationNumber, string? TaxId, string? Type = null, string? ContactEmail = null);
public sealed record HoursWindow(string? Start, string? End);
// Days: the open days, each with the usual (Weekdays) windows; Saturday keeps its own windows when they are given. Null = Monday to Friday
// plus Saturday when it has windows (drafts saved before the day switches).
public sealed record OnboardingHours(IReadOnlyList<HoursWindow>? Weekdays, IReadOnlyList<HoursWindow>? Saturday, int? SlotMinutes, long? NewVisitFee, long? FollowUpFee, IReadOnlyList<DayOfWeek>? Days = null);
public sealed record OnboardingTeamMember(string? Email, Role? Role);
// Patients: how the practice brings its existing patients (OnboardingRules.PatientStarts); null when the step was skipped.
public sealed record OnboardingData(
    OnboardingProfile? Profile, OnboardingLicence? Licence, OnboardingClinic? Clinic, OnboardingHours? Hours,
    IReadOnlyList<string>? Templates, IReadOnlyList<OnboardingTeamMember>? Team, string? Patients = null);

/// <summary>One invalid field. Field is the path in <see cref="OnboardingData"/> (e.g. "licence.number"); Code a stable key the UI translates.</summary>
public sealed record OnboardingError(string Field, string Code);

public sealed record ProvisionedPractice(Guid TenantId, Guid FacilityId, Guid MembershipId, Role Role);

/// <summary>Creates a practice from a finished, validated draft (MDV-P1-004).</summary>
public interface IPracticeProvisioning
{
    /// <summary>
    /// In one transaction: tenant (bound to the country), facility, owner membership, practice profile, hours and fee defaults and,
    /// for a doctor, the submitted licence (verification pending). The creation is audited in the new tenant before it commits.
    /// The team step's invitations are sent afterwards through <see cref="IInvitations"/>, inside the new tenant (the caller enters it).
    /// </summary>
    Task<ProvisionedPractice> CreateAsync(string country, OnboardingData data, CancellationToken cancellationToken = default);
}
