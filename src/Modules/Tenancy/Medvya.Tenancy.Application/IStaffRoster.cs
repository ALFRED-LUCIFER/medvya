using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>
/// One person (or pending invitee) of the practice. <see cref="Email"/> is set only while invited; a name comes from the identity
/// module by <see cref="UserId"/>. <see cref="InvitationState"/> is pending, expired or revoked, and only for an Invited membership.
/// </summary>
public sealed record StaffRow(
    Guid MembershipId, Guid? UserId, string? Email, Role Role, Guid? CustomRoleId, string? CustomRoleName, Guid FacilityId, string FacilityName,
    MembershipStatus Status, Guid? InvitationId, string? InvitationState, DateTimeOffset? InvitationExpiresAt);

public sealed record CustomRoleView(Guid Id, string Name, Role BaseRole, IReadOnlyList<Capability> Capabilities);

public sealed record SaveCustomRole
{
    public string? Name { get; init; }
    public Role BaseRole { get; init; }
    public IReadOnlyList<Capability>? Capabilities { get; init; }
}

/// <summary>Staff list and practice-defined roles (MDV-P1-008). Each role change saves its before/after and audit event in one transaction.</summary>
public interface IStaffRoster
{
    Task<IReadOnlyList<StaffRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomRoleView>> RolesAsync(CancellationToken cancellationToken = default);

    /// <exception cref="SettingsValidationException">Bad name, a base role that cannot be assigned, a duplicate name, or capabilities beyond <see cref="CustomRoleRules.Extras"/>.</exception>
    Task<CustomRoleView> CreateRoleAsync(SaveCustomRole command, CancellationToken cancellationToken = default);

    /// <summary>Same checks as create. Null when the role is not the tenant's.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<CustomRoleView?> UpdateRoleAsync(Guid id, SaveCustomRole command, CancellationToken cancellationToken = default);
}
