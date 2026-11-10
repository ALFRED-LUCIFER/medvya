using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>A facility of the current tenant. TimeZone is an IANA id (Asia/Kolkata, Asia/Dubai).</summary>
public sealed record FacilityInfo(Guid Id, string Name, string TimeZone);

/// <summary>What a printed prescription shows about its doctor (MDV-P2-017): the profile's name and qualifications and the licence the owner attested (ADR-016).</summary>
public sealed record PrescriberInfo(string FullName, string? Qualifications, string LicenceNumber, string LicenceAuthority);

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

    /// <summary>The doctor's profile and licence for their membership at this facility; null when that membership has none (not a doctor).</summary>
    Task<PrescriberInfo?> PrescriberAsync(Guid userId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Holidays of the facility between two facility-local dates (inclusive), name by date. Bookings on these dates are refused (MDV-P1-005).</summary>
    Task<IReadOnlyDictionary<DateOnly, string>> ClosuresAsync(Guid facilityId, DateOnly first, DateOnly last, CancellationToken cancellationToken = default);
}
