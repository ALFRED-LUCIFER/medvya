using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>
/// The clinic's public face (MDV-P1-005): contact and registration details printed on prescriptions and invoices, and the logo.
/// One row per tenant; the clinic name itself is <see cref="Tenant.Name"/>. Business details only, never a patient's.
/// </summary>
public sealed class PracticeProfile
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Public)] public string? Address { get; set; }
    /// <summary>Clinic registration (e.g. clinical establishment or DoH facility number). A business number, not a person's identifier.</summary>
    [Classification(DataClass.Public)] public string? RegistrationNumber { get; set; }
    /// <summary>The last valid day of that registration; owners are reminded 60/30/7/0 days before (MDV-P1-010). Null when it does not expire or is not entered.</summary>
    [Classification(DataClass.Internal)] public DateOnly? RegistrationExpiresOn { get; set; }
    /// <summary>GSTIN (India) or TRN (UAE) of the business.</summary>
    [Classification(DataClass.Public)] public string? TaxNumber { get; set; }
    [Classification(DataClass.Public)] public string? Phone { get; set; }
    [Classification(DataClass.Public)] public string? Email { get; set; }
    /// <summary>Random blob key of the logo (no names in paths); null without a logo.</summary>
    [Classification(DataClass.Internal)] public string? LogoKey { get; set; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>Informational opening hours of a location on one weekday; a weekday without a row is closed. Bookable time stays in the schedule templates.</summary>
public sealed class OperatingHours
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>0 = Sunday … 6 = Saturday (System.DayOfWeek).</summary>
    [Classification(DataClass.Internal)] public DayOfWeek Weekday { get; init; }
    [Classification(DataClass.Internal)] public TimeOnly Opens { get; init; }
    [Classification(DataClass.Internal)] public TimeOnly Closes { get; init; }
}

/// <summary>A holiday: the location takes no new bookings on this facility-local date.</summary>
public sealed class Closure
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public DateOnly Date { get; init; }
    /// <summary>e.g. "Diwali". Names an occasion, never a person.</summary>
    [Classification(DataClass.Public)] public required string Name { get; set; }
}

/// <summary>
/// Before and after of one settings change (MDV-P1-005). The audit event (settings.*) names the action and points here by
/// <see cref="Id"/>; the audit trail itself carries ids and codes only. Values are business settings, JSON of the changed fields.
/// </summary>
public sealed class SettingsChange
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid? ActorUserId { get; init; }
    [Classification(DataClass.Internal)] public required string Section { get; init; }
    [Classification(DataClass.Internal)] public Guid? ResourceId { get; init; }
    [Classification(DataClass.Personal)] public string? Before { get; init; }
    [Classification(DataClass.Personal)] public string? After { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset OccurredAt { get; init; }
}
