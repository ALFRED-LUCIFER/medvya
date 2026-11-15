using Medvya.Tenancy.Domain;

namespace Medvya.Tenancy.Application;

/// <summary>
/// The features of the current tenant (MDV-P0-028, MDV-P5-041), evaluated server-side on every call. A feature is on only when Medvya's release reaches
/// the organisation (read with a cache of at most 60 s) and, for a feature with a clinic switch, the practice has turned it on (read on every call).
/// </summary>
public interface IFeatureFlags
{
    /// <summary>False when either switch is off, the clinic switch has no row (off by default) or there is no current tenant.</summary>
    Task<bool> IsEnabledAsync(Feature feature, CancellationToken cancellationToken = default);

    /// <summary>Whether Medvya's release reaches the current tenant, whatever the clinic's own switch says (the owner's switch is shown only then).</summary>
    Task<bool> IsReleasedAsync(Feature feature, CancellationToken cancellationToken = default);

    /// <summary>Keys of the current tenant's enabled switchable features (empty without a tenant; always-on ones are left out), for the client (/v1/me/context).</summary>
    Task<IReadOnlyList<string>> EnabledKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or changes the current tenant's own switch and audits the change. Takes effect on the next request.</summary>
    /// <exception cref="ArgumentException">The feature has no clinic switch, or the owner or description is missing.</exception>
    Task SetAsync(Feature feature, bool enabled, string owner, string description, CancellationToken cancellationToken = default);
}

/// <summary>A feature's release in one country, as the platform console shows it. <see cref="IsNew"/>: Medvya has recorded no release yet (its initial stage applies).</summary>
public sealed record FeatureReleaseView(string Key, bool Clinical, bool ClinicSwitch, bool AlwaysOn, ReleaseStage Stage, bool IsNew, int Percentage,
    IReadOnlyList<Guid> Organisations, string? ClinicalSignOff, DateTimeOffset? ChangedAt);

/// <summary>A new release stage. <see cref="ClinicalSignOff"/>: records the sign-off (who signed, the reference); once recorded it stays.</summary>
public sealed record ReleaseChange(ReleaseStage Stage, int? Percentage, IReadOnlyList<Guid>? Organisations, string? ClinicalSignOff);

/// <summary>
/// The platform console's way to Medvya's release switches (MDV-P5-041), one database per country through the narrow login medvya_features. No authorisation
/// here; the console endpoints hold the policy and write the platform audit entry.
/// </summary>
public interface IFeatureReleaseConsole
{
    /// <summary>The countries the console can reach (a database login is configured for each).</summary>
    IReadOnlyList<string> Countries { get; }

    /// <summary>Every listed feature available in the country; null when the country is not reachable.</summary>
    Task<IReadOnlyList<FeatureReleaseView>?> ListAsync(string country, CancellationToken cancellationToken = default);

    /// <summary>Saves the release and returns its row id; null when the country is not reachable or the feature is not listed for it.</summary>
    /// <exception cref="SettingsValidationException">
    /// "stage:locked" (always on), "clinicalSignOff:required" (a clinical feature to everyone without a sign-off), "percentage:range" (1 to 99),
    /// "organisations:required" (beta with none), "organisations:too_many", "clinicalSignOff:too_long", "stage:invalid".
    /// </exception>
    Task<Guid?> SetAsync(string country, string key, ReleaseChange change, Guid operatorId, CancellationToken cancellationToken = default);
}
