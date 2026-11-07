using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>One active person at the review, with the decision so far (null = not yet decided). The sign-in time is added by the host (identity module).</summary>
public sealed record AccessReviewMember(Guid MembershipId, Guid UserId, Role Role, Guid FacilityId, string FacilityName, AccessDecision? Decision);

/// <summary>The current quarter's review. <see cref="LastCompletedAt"/> is the newest completion in any quarter (shown in settings).</summary>
public sealed record AccessReviewView(string Quarter, DateTimeOffset? CompletedAt, DateTimeOffset? LastCompletedAt, IReadOnlyList<AccessReviewMember> Members);

/// <summary>
/// Quarterly access review (MDV-P1-010). Each decision saves, completes the review when it was the last open one, and audits, in one
/// transaction. Offboarding a person goes through the staff roster (which ends their sessions); the host then records it here.
/// </summary>
public interface IAccessReviews
{
    /// <summary>The review of the current quarter (opened if needed) with every active member and their decision.</summary>
    Task<AccessReviewView> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Confirms an active member's access. False when the membership is not an active member of the tenant.</summary>
    Task<bool> ConfirmAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>Records that the member was offboarded in this review (call after the roster offboarded them).</summary>
    Task RecordOffboardedAsync(Guid membershipId, CancellationToken cancellationToken = default);
}

public sealed record ExpiringLicence(Guid MembershipId, Guid FacilityId, DateOnly ExpiresOn);

/// <summary>What the compliance reminders need from one tenant: the practice's name and contact address, licences expiring within 60 days (or already expired), and whether a review is due.
/// <paramref name="RegistrationExpiresOn"/>: the practice's own registration, only when it expires within 60 days (or already has).</summary>
public sealed record ComplianceFacts(string ClinicName, string? ContactEmail, IReadOnlyList<ExpiringLicence> Licences, string? ReviewDueQuarter, IReadOnlyList<Guid> FacilityIds, DateOnly? RegistrationExpiresOn = null);

public interface IComplianceFacts
{
    /// <summary>Reads the facts for the current tenant.</summary>
    Task<ComplianceFacts> ReadAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Every tenant the background job should visit (ids only; tenants created before the index existed are missing until re-indexed).</summary>
    Task<IReadOnlyList<Guid>> TenantsAsync(CancellationToken cancellationToken = default);
}

public static class AccessReviewQuarter
{
    /// <summary>"2026-Q4": the calendar quarter of the date.</summary>
    public static string Of(DateOnly date) => $"{date.Year}-Q{(date.Month - 1) / 3 + 1}";
}
