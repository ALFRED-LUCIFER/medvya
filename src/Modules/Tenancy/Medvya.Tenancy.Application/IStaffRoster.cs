using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>
/// One person (or pending invitee) of the practice. <see cref="Email"/> is set only while invited; a name comes from the identity
/// module by <see cref="UserId"/>. <see cref="InvitationState"/> is pending, expired or revoked, and only for an Invited membership.
/// </summary>
public sealed record StaffRow(
    Guid MembershipId, Guid? UserId, string? Email, Role Role, Guid? CustomRoleId, string? CustomRoleName, Guid FacilityId, string FacilityName,
    MembershipStatus Status, Guid? InvitationId, string? InvitationState, DateTimeOffset? InvitationExpiresAt, bool IsOwner = false);

public sealed record CustomRoleView(Guid Id, string Name, Role BaseRole, IReadOnlyList<Capability> Capabilities);

public sealed record SaveCustomRole
{
    public string? Name { get; init; }
    public Role BaseRole { get; init; }
    public IReadOnlyList<Capability>? Capabilities { get; init; }
}

/// <summary>How an offboarding ended. <see cref="UserId"/> is the person whose sessions the caller must now end (null for a never-accepted invitee).</summary>
/// <param name="MembershipIds">Every membership ended: the person's memberships at all locations the caller administers (MDV-P1-071).</param>
/// <param name="OpenVisits">Patients already checked in for the person today (waiting or in consultation): reception reassigns them on the board.</param>
public sealed record OffboardResult(OffboardOutcome Outcome, Guid? UserId = null, int OpenVisits = 0, IReadOnlyList<Guid>? MembershipIds = null);

/// <summary>Another module's share of an offboarding: what to run after the commit, and how many open items it left for reception.</summary>
public sealed record OffboardingShare(Func<Task>? AfterCommit, int OpenItems = 0);

/// <summary>NotOwner: an owner or a doctor, offboarded by someone who is not an owner (MDV-P1-107). LastOwner: nobody would own the practice.</summary>
public enum OffboardOutcome { Done, NotFound, LastAdmin, NotOwner, LastOwner }

public enum ReactivateOutcome { Done, NotFound, AboveGranter }

public enum LocationsOutcome { Done, NotFound, AboveGranter, LastAdmin, NotOwner }

/// <summary>
/// How a role change ended (MDV-P1-107). NotAssignable: the person is an owner or a doctor, or the new role is not one the staff table assigns;
/// the caller ends the person's sessions on that membership when Done.
/// </summary>
public enum ChangeRoleOutcome { Done, NotFound, NotAssignable, AboveGranter, LastAdmin }

public sealed record ChangeRoleResult(ChangeRoleOutcome Outcome, Guid? UserId = null);

/// <param name="Removed">Memberships ended by taking locations away, whose sessions the caller must now end for <paramref name="UserId"/>.</param>
public sealed record LocationsResult(LocationsOutcome Outcome, Guid? UserId = null, IReadOnlyList<Guid>? Removed = null);

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

    /// <summary>
    /// Offboards a membership of the current tenant (MDV-P1-009), and the same person's memberships at the other locations the caller administers (MDV-P1-071): status Offboarded, pending invitations revoked, reason and actor kept
    /// (SettingsChange "staff.offboard") and audited, all in one transaction. Refuses the last active staff administrator. An already
    /// offboarded membership is NotFound. The caller ends the person's sessions in this tenant.
    /// </summary>
    /// <param name="offerRebooking">The admin's choice for the doctor's future visits: true sends each patient the rebooking message, false
    /// moves them quietly for reception to call (owner decision, #225).</param>
    /// <exception cref="SettingsValidationException">No reason, or one over 500 characters.</exception>
    Task<OffboardResult> OffboardAsync(Guid membershipId, string? reason, bool offerRebooking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the locations an active member works at (MDV-P1-071): a location added gets the same role there (a doctor, their own profile and licence
    /// copy) and takes no new plan seat; a location left out ends there (needs a reason). The caller must administer staff at every location changed.
    /// </summary>
    /// <exception cref="SettingsValidationException">No locations, or no reason when one is taken away.</exception>
    Task<LocationsResult> SetLocationsAsync(Guid membershipId, IReadOnlyList<Guid> facilityIds, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings an offboarded member back (MDV-P1-009): status Active again with the same role, reason and actor kept and audited. Only someone
    /// who administers staff at that facility, and who could grant that role today, may do it. Sessions are not restored: the person signs in.
    /// </summary>
    /// <exception cref="SettingsValidationException">No reason, or one over 500 characters.</exception>
    Task<ReactivateOutcome> ReactivateAsync(Guid membershipId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an active member's role in place (MDV-P1-107): receptionist, nurse, care coordinator or practice admin, with an optional practice-defined
    /// role on that base. Same ceiling as inviting; never the last staff administrator; never an owner or a doctor. Before/after (SettingsChange
    /// "staff.member.role") and the audit event "staff.role.change" (from and to) commit with it. Access follows on the next request (roles are read live).
    /// </summary>
    /// <exception cref="SettingsValidationException">A practice-defined role that is not the tenant's or not on that base role.</exception>
    Task<ChangeRoleResult> ChangeRoleAsync(Guid membershipId, Role role, Guid? customRoleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Another module's part of offboarding (MDV-P1-009), run inside the roster's transaction on its connection so it commits or rolls back
/// with the membership change. Returns what to do after the commit (live notifications) and the open items it left.
/// </summary>
public interface IStaffOffboardingParticipant
{
    /// <summary><paramref name="facilityId"/> is the offboarded membership's facility: the person may still work at the practice's other sites (#877).</summary>
    Task<OffboardingShare> OffboardedAsync(Guid doctorUserId, Guid facilityId, bool offerRebooking, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken);
}
