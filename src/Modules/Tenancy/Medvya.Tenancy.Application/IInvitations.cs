using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

public sealed record InvitationCreated(Guid Id, DateTimeOffset ExpiresAt, bool LicenceVerificationRequired);

/// <summary>What the invitee sees on /invite/:token (F-06). No email address: the link holder does not need it.</summary>
public sealed record InvitationPreview(
    string ClinicName, string FacilityName, Guid InvitedBy, Role Role, DateTimeOffset ExpiresAt, bool LicenceVerificationRequired);

public sealed record FacilityOption(Guid Id, string Name);

/// <summary>Staff invitations (MDV-P0-013). Every change is audited; a change that cannot be audited does not happen.</summary>
public interface IInvitations
{
    /// <summary>The current tenant's facilities an invitation can be scoped to.</summary>
    Task<IReadOnlyList<FacilityOption>> FacilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an Invited membership and its invitation in the current tenant, invited by the current user, and emails the link.
    /// Null when the facility is not one of the tenant's. Throws ArgumentException for an invalid email or a role that cannot be invited.
    /// </summary>
    Task<InvitationCreated?> CreateAsync(string email, Role role, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a pending invitation of the current tenant. False when there is none (unknown, used, revoked).</summary>
    Task<bool> RevokeAsync(Guid invitationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The pending invitation for a token, or null when it is unknown, expired, revoked or used.
    /// Runs in the token's tenant: the caller enters that tenant (InvitationToken.Read) before resolving this service.
    /// </summary>
    Task<InvitationPreview?> PreviewAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses the token once: in one transaction the invitation is marked used and the membership gets this user and becomes Active.
    /// The acceptance is audited before that transaction commits (fail closed).
    /// False when the token is not pending (unknown, expired, revoked, already used) or the user already holds a membership there.
    /// Runs in the token's tenant, like <see cref="PreviewAsync"/>.
    /// </summary>
    Task<bool> AcceptAsync(string token, Guid userId, CancellationToken cancellationToken = default);
}
