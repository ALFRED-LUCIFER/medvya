namespace Medvya.Tenancy.Application;

/// <summary>Per-tenant feature flags of the current tenant, evaluated server-side on every call (no cache).</summary>
public interface IFeatureFlags
{
    /// <summary>False when the flag is off, missing (flags default to off) or there is no current tenant.</summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates or changes the current tenant's flag and audits the change. Takes effect on the next request.</summary>
    Task SetAsync(string key, bool enabled, string owner, string description, CancellationToken cancellationToken = default);
}
