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

/// <summary>
/// A user's role at one facility of one tenant. One person can hold memberships in several clinics.
/// An Invited membership has no user yet; accepting its invitation sets the user and makes it Active (MDV-P0-013).
/// </summary>
public sealed class Membership
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid? UserId { get; set; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Role Role { get; set; }
    [Classification(DataClass.Internal)] public MembershipStatus Status { get; set; }
}

/// <summary>
/// A per-tenant switch for one feature (PRD v2 §08). A flag with no row is off, so new features default to off.
/// Owner and description name teams and features, never people or patients.
/// </summary>
public sealed class FeatureFlag
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>Stable lowercase key, e.g. "scheduling.waitlist".</summary>
    [Classification(DataClass.Internal)] public required string Key { get; init; }
    [Classification(DataClass.Internal)] public bool Enabled { get; set; }
    /// <summary>Owning team, accountable for the flag and its removal.</summary>
    [Classification(DataClass.Internal)] public required string Owner { get; set; }
    [Classification(DataClass.Internal)] public required string Description { get; set; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}
