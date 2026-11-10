using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>
/// What the signed-in staff member may do at the facility they are working at, read from the database on every call so a role
/// change, suspension or offboarding takes effect on the next request (PRD v2 P0-05).
/// </summary>
public interface IStaffAccess
{
    /// <summary>
    /// Access to one capability from the role of the user's <b>active</b> membership at the current tenant and facility (RoleMatrix).
    /// <see cref="Access.None"/> without a user, tenant, facility or active membership there.
    /// </summary>
    Task<Access> AccessAsync(Capability capability, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="AccessAsync(Capability, CancellationToken)"/> for an explicit facility of the tenant (scheduling and messages take
    /// the facility from the request). Includes custom-role extras and the organisation-state gate; <see cref="Access.None"/> without an active membership there.
    /// </summary>
    Task<Access> AccessAsync(Capability capability, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The same for several capabilities at once (in their order), reading the membership, organisation state and custom role once.</summary>
    Task<IReadOnlyList<Access>> AccessAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The access the member's role holds at <paramref name="facilityId"/> before the organisation's state narrows it. Only for the check that a grant
    /// never exceeds the granter's own access (invitations, custom roles): that comparison must not change while an organisation is set-up only.
    /// </summary>
    Task<IReadOnlyList<Access>> RoleAccessAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The role of the user's active membership at the current facility (database), or null. For display and audit, not for access: use <see cref="AccessAsync(Capability, CancellationToken)"/>.</summary>
    Task<Role?> CurrentRoleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The current organisation's state. <see cref="AccessAsync(Capability, CancellationToken)"/> already applies it (<see cref="OrganisationRules"/>),
    /// so a patient, booking, note or signing endpoint is refused before approval (ADR-016, MDV-P1-063); this is for the shell notice.
    /// </summary>
    Task<OrganisationState> OrganisationStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Country of the current tenant ("IN" or "AE"); selects the country rules. Null without a tenant.</summary>
    Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default);
}
