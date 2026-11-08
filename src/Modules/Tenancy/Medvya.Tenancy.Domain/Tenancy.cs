using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>A clinic organisation; bound to one country data plane at creation (ADR-003).</summary>
public sealed class Tenant
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Public)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public required string CountryCode { get; init; }
    [Classification(DataClass.Internal)] public required string ResidencyRegion { get; init; }
    /// <summary>Staff sessions end after this many idle minutes (MDV-P0-012). 15 suits shared clinic PCs; 1–480.</summary>
    [Classification(DataClass.Internal)] public int IdleTimeoutMinutes { get; set; } = 15;
    /// <summary>Which clinicians treat the facility's patients (MDV-P2-024); a setting of the practice owner.</summary>
    [Classification(DataClass.Internal)] public ClinicalAccessPolicy ClinicalAccess { get; set; }
    /// <summary>When the practice was created; the first quarterly access review is due a quarter later (#912). Tenants older than the column read 0001-01-01.</summary>
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

public sealed class Facility
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Public)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public required string TimeZone { get; set; }
    /// <summary>Street address of the location, printed on invoices and prescriptions (MDV-P1-005).</summary>
    [Classification(DataClass.Public)] public string? Address { get; set; }
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
    /// <summary>The practice-defined role this person holds (its base role is <see cref="Role"/>); null for a built-in role (MDV-P1-008).</summary>
    [Classification(DataClass.Internal)] public Guid? CustomRoleId { get; set; }
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

/// <summary>
/// The practice owner's acceptance of the Medvya Terms of Service and the clinic Data Processing Agreement (MDV-P1-019), one
/// immutable row (a ledger table: SQL Server rejects UPDATE and DELETE). A row for the current terms version activates the tenant.
/// </summary>
public sealed class TermsAcceptance
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    /// <summary>The wording version accepted; booking and sending need the current one.</summary>
    [Classification(DataClass.Internal)] public required string TermsVersion { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset AcceptedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid AcceptedBy { get; init; }
}
