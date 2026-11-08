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
    /// the facility from the request). Includes custom-role extras and the licence gate; <see cref="Access.None"/> without an active membership there.
    /// </summary>
    Task<Access> AccessAsync(Capability capability, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The same for several capabilities at once (in their order), reading the membership, licence and custom role once.</summary>
    Task<IReadOnlyList<Access>> AccessAsync(IReadOnlyList<Capability> capabilities, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The role of the user's active membership at the current facility (database), or null. For display and audit, not for access: use <see cref="AccessAsync(Capability, CancellationToken)"/>.</summary>
    Task<Role?> CurrentRoleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True while the active membership is a doctor whose licence is not verified (pending, rejected or never submitted): recording and signing
    /// capabilities are then <see cref="Access.None"/> in <see cref="AccessAsync"/> (MDV-P1-004, feature-level, not only the UI).
    /// </summary>
    Task<bool> LicencePendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Country of the current tenant ("IN" or "AE"); selects the country rules. Null without a tenant.</summary>
    Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default);
}
