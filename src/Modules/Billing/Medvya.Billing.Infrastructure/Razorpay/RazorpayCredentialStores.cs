using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Medvya.Billing.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Medvya.Billing.Infrastructure.Razorpay;

/// <summary>
/// Key Vault (the payments vault, Razorpay:KeyVaultUri). Secret razorpay-{tenantId} holds the clinic's keys as JSON; secret
/// razorpay-hook-{webhookKey} holds the tenant id, so a webhook finds its tenant without the database. Reads are cached for five minutes
/// so webhooks do not call Key Vault each time.
/// shortcut: the cache is per instance, so a key change saved on one instance reaches another within five minutes; fine while App Service
/// runs one instance, upgrade to a shared invalidation when it scales out.
/// </summary>
public sealed class KeyVaultRazorpayCredentialStore(SecretClient vault, TimeProvider clock) : IRazorpayCredentialStore
{
    static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    sealed record Stored(string KeyId, string KeySecret, string WebhookSecret, string WebhookKey);
    readonly ConcurrentDictionary<Guid, (RazorpayCredentials Value, DateTimeOffset Until)> _byTenant = new();
    readonly ConcurrentDictionary<string, (Guid Tenant, DateTimeOffset Until)> _byHook = new(StringComparer.Ordinal);

    public static string SecretName(Guid tenantId) => $"razorpay-{tenantId}";
    public static string HookSecretName(string webhookKey) => $"razorpay-hook-{webhookKey}";

    public async Task<RazorpayCredentials?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        if (_byTenant.TryGetValue(tenantId, out var hit) && hit.Until > clock.GetUtcNow()) return hit.Value;
        var json = await ReadAsync(SecretName(tenantId), ct);
        if (json is null) return null;
        var s = JsonSerializer.Deserialize<Stored>(json)!;
        return Remember(tenantId, new(s.KeyId, s.KeySecret, s.WebhookSecret, s.WebhookKey));
    }

    // shortcut: an unknown well-formed key still reads Key Vault each time; the webhook endpoint (part 2) gets a rate limit.
    public async Task<(Guid TenantId, RazorpayCredentials Credentials)?> FindByWebhookKeyAsync(string webhookKey, CancellationToken ct)
    {
        if (!RazorpayCredentials.IsWebhookKey(webhookKey)) return null;
        Guid tenant;
        if (_byHook.TryGetValue(webhookKey, out var hit) && hit.Until > clock.GetUtcNow()) tenant = hit.Tenant;
        else if (Guid.TryParse(await ReadAsync(HookSecretName(webhookKey), ct), out tenant)) _byHook[webhookKey] = (tenant, clock.GetUtcNow() + CacheFor);
        else return null;
        var creds = await GetAsync(tenant, ct);
        return creds is not null && creds.WebhookKey == webhookKey ? (tenant, creds) : null;
    }

    public async Task<RazorpayCredentials> SaveAsync(Guid tenantId, string keyId, string keySecret, string webhookSecret, CancellationToken ct)
    {
        _byTenant.TryRemove(tenantId, out _);
        var webhookKey = (await GetAsync(tenantId, ct))?.WebhookKey;
        if (webhookKey is null)
        {
            webhookKey = RazorpayCredentials.NewWebhookKey();
            await vault.SetSecretAsync(HookSecretName(webhookKey), tenantId.ToString(), ct); // pointer first: a half save leaves no tenant without its hook
        }
        await vault.SetSecretAsync(SecretName(tenantId), JsonSerializer.Serialize(new Stored(keyId, keySecret, webhookSecret, webhookKey)), ct);
        return Remember(tenantId, new(keyId, keySecret, webhookSecret, webhookKey));
    }

    RazorpayCredentials Remember(Guid tenantId, RazorpayCredentials c)
    {
        _byTenant[tenantId] = (c, clock.GetUtcNow() + CacheFor);
        return c;
    }

    async Task<string?> ReadAsync(string name, CancellationToken ct)
    {
        try { return (await vault.GetSecretAsync(name, cancellationToken: ct)).Value.Value; }
        catch (RequestFailedException e) when (e.Status == 404) { return null; }
    }
}

/// <summary>
/// Development only: Razorpay TEST keys from configuration (user secrets) under Razorpay:Tenants:{tenantId}:KeyId, KeySecret, WebhookSecret,
/// WebhookKey. Refuses to exist outside Development.
/// shortcut: keys saved from Settings are kept in memory until the API restarts; put them in user secrets to keep them.
/// </summary>
public sealed class ConfigurationRazorpayCredentialStore : IRazorpayCredentialStore
{
    readonly IConfiguration _config;
    readonly ConcurrentDictionary<Guid, RazorpayCredentials> _saved = new();

    public ConfigurationRazorpayCredentialStore(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsDevelopment()) throw new InvalidOperationException("Razorpay keys come from Key Vault outside Development; set Razorpay:KeyVaultUri.");
        _config = config;
    }

    public Task<RazorpayCredentials?> GetAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Get(tenantId));

    public Task<(Guid TenantId, RazorpayCredentials Credentials)?> FindByWebhookKeyAsync(string webhookKey, CancellationToken ct)
    {
        var tenants = _saved.Keys.Concat(_config.GetSection("Razorpay:Tenants").GetChildren().Select(s => Guid.TryParse(s.Key, out var t) ? t : Guid.Empty));
        foreach (var tenant in tenants.Distinct())
            if (RazorpayCredentials.IsWebhookKey(webhookKey) && Get(tenant) is { } c && c.WebhookKey == webhookKey)
                return Task.FromResult<(Guid, RazorpayCredentials)?>((tenant, c));
        return Task.FromResult<(Guid, RazorpayCredentials)?>(null);
    }

    public Task<RazorpayCredentials> SaveAsync(Guid tenantId, string keyId, string keySecret, string webhookSecret, CancellationToken ct)
    {
        var kept = Get(tenantId)?.WebhookKey;
        var saved = new RazorpayCredentials(keyId, keySecret, webhookSecret, RazorpayCredentials.IsWebhookKey(kept) ? kept! : RazorpayCredentials.NewWebhookKey());
        _saved[tenantId] = saved;
        return Task.FromResult(saved);
    }

    RazorpayCredentials? Get(Guid tenantId)
    {
        if (_saved.TryGetValue(tenantId, out var saved)) return saved;
        var s = _config.GetSection($"Razorpay:Tenants:{tenantId}");
        return string.IsNullOrEmpty(s["KeyId"]) || string.IsNullOrEmpty(s["KeySecret"])
            ? null
            : new(s["KeyId"]!, s["KeySecret"]!, s["WebhookSecret"] ?? "", s["WebhookKey"] ?? "");
    }
}
