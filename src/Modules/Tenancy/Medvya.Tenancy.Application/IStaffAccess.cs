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

    /// <summary>Country of the current tenant ("IN" or "AE"); selects the country rules. Null without a tenant.</summary>
    Task<string?> TenantCountryAsync(CancellationToken cancellationToken = default);
}
