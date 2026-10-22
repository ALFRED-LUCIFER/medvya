using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>A facility of the current tenant. TimeZone is an IANA id (Asia/Kolkata, Asia/Dubai).</summary>
public sealed record FacilityInfo(Guid Id, string Name, string TimeZone);

/// <summary>
/// Facilities and memberships of the current tenant, for other modules (they never read Tenancy's tables).
/// Tenant query filters and row-level security apply, so another tenant's facility or membership is never found.
/// </summary>
public interface IFacilityDirectory
{
    Task<FacilityInfo?> FindAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>The role of the user's active membership at this facility, or null: role alone never grants another facility.</summary>
    Task<Role?> ActiveRoleAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>User ids of the active doctor memberships (solo or hospital doctor) at this facility.</summary>
    Task<IReadOnlyList<Guid>> ActiveDoctorsAsync(Guid facilityId, CancellationToken cancellationToken = default);
}
