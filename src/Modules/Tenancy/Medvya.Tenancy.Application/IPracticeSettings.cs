using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>The clinic profile. Name is the tenant's name; HasLogo says whether a logo link can be requested.</summary>
public sealed record ProfileView(string Name, string? Address, string? RegistrationNumber, string? TaxNumber, string? Phone, string? Email, bool HasLogo, DateOnly? RegistrationExpiresOn = null);

public sealed record SaveProfile
{
    public string? Name { get; init; }
    public string? Address { get; init; }
    public string? RegistrationNumber { get; init; }
    public DateOnly? RegistrationExpiresOn { get; init; }
    public string? TaxNumber { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

public sealed record LocationView(Guid Id, string Name, string? Address, string TimeZone);

public sealed record SaveLocation
{
    public string? Name { get; init; }
    public string? Address { get; init; }
    /// <summary>IANA id (Asia/Kolkata, Asia/Dubai); required for a new location, unchanged when null on an update.</summary>
    public string? TimeZone { get; init; }
}

/// <summary>Opening hours of one weekday in facility wall-clock time. A weekday without an entry is closed.</summary>
public sealed record DayHours(DayOfWeek Weekday, TimeOnly Opens, TimeOnly Closes);

public sealed record ClosureView(Guid Id, Guid FacilityId, DateOnly Date, string Name);

public sealed record AddClosure
{
    public Guid FacilityId { get; init; }
    public DateOnly Date { get; init; }
    public string? Name { get; init; }
}

public sealed record ValidationError(string Field, string Code);

/// <summary>Input was rejected. Field/code pairs only, never the entered values.</summary>
public sealed class SettingsValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("Settings input is invalid: " + string.Join(", ", errors.Select(e => $"{e.Field}:{e.Code}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;

    public static void ThrowIfAny(List<ValidationError> errors)
    {
        if (errors.Count > 0) throw new SettingsValidationException(errors);
    }
}

/// <summary>
/// Practice settings (MDV-P1-005) of the current tenant. The caller's capability is checked by the endpoints; tenant query
/// filters and row-level security scope every row. Each change writes a <c>settings.*</c> audit event and its before/after in
/// the same transaction as the change, so a change that cannot be audited does not happen.
/// </summary>
public interface IPracticeSettings
{
    Task<ProfileView> GetProfileAsync(CancellationToken cancellationToken = default);

    /// <exception cref="SettingsValidationException"/>
    Task<ProfileView> SaveProfileAsync(SaveProfile command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocationView>> ListLocationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a location; the caller gets the same role there, so they can work at it.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<LocationView> AddLocationAsync(SaveLocation command, CancellationToken cancellationToken = default);

    /// <summary>Null when the location is not one of the tenant's.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<LocationView?> UpdateLocationAsync(Guid facilityId, SaveLocation command, CancellationToken cancellationToken = default);

    /// <summary>Null when the location is not one of the tenant's.</summary>
    Task<IReadOnlyList<DayHours>?> GetHoursAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the opening hours; null when the location is not one of the tenant's.</summary>
    /// <exception cref="SettingsValidationException"/>
    Task<IReadOnlyList<DayHours>?> SetHoursAsync(Guid facilityId, IReadOnlyList<DayHours> hours, CancellationToken cancellationToken = default);

    /// <summary>Closures of the location, by date; null when the location is not one of the tenant's.</summary>
    Task<IReadOnlyList<ClosureView>?> ListClosuresAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <summary>Null when the location is not one of the tenant's.</summary>
    /// <exception cref="SettingsValidationException">Name missing or too long, or the date already closed ("duplicate").</exception>
    Task<ClosureView?> AddClosureAsync(AddClosure command, CancellationToken cancellationToken = default);

    Task<bool> RemoveClosureAsync(Guid closureId, CancellationToken cancellationToken = default);

    /// <summary>Stores a new logo, replacing the old one. Throws <see cref="SettingsValidationException"/> for an unsupported type or size.</summary>
    Task SetLogoAsync(byte[] content, CancellationToken cancellationToken = default);

    Task<bool> RemoveLogoAsync(CancellationToken cancellationToken = default);

    /// <summary>A link to the logo that stops working after <paramref name="lifetime"/>; null without a logo.</summary>
    Task<Uri?> LogoLinkAsync(TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>Which clinicians treat the facility's patients (MDV-P2-024).</summary>
    Task<ClinicalAccessPolicy> GetClinicalAccessAsync(CancellationToken cancellationToken = default);

    /// <exception cref="SettingsValidationException">Not a known policy.</exception>
    Task<ClinicalAccessPolicy> SaveClinicalAccessAsync(ClinicalAccessPolicy policy, CancellationToken cancellationToken = default);
}
