using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>A clinic organisation; bound to one country data plane at creation (ADR-003).</summary>
public sealed class Tenant
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Public)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public required string CountryCode { get; init; }
    [Classification(DataClass.Internal)] public required string ResidencyRegion { get; init; }
}

public sealed class Facility
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Public)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public required string TimeZone { get; set; }
}

public enum MembershipStatus { Invited, Active, Suspended, Offboarded }

/// <summary>A user's role at one facility of one tenant. One person can hold memberships in several clinics.</summary>
public sealed class Membership
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Role Role { get; set; }
    [Classification(DataClass.Internal)] public MembershipStatus Status { get; set; }
}
