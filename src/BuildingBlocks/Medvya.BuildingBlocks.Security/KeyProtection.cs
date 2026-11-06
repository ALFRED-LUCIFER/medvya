using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Security;

/// <summary>Thrown when a value or key cannot be protected or read. Messages are fixed text: never an identifier, value or key.</summary>
public sealed class KeyProtectionException : Exception
{
    public const string KeysUnavailable = "The key service is unavailable; the operation was refused.";
    public const string UnreadableValue = "The protected value is invalid or belongs to another tenant.";
    public const string NoTenant = "No tenant is set; the operation was refused.";

    public KeyProtectionException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class NationalIdProtectionOptions
{
    public const string Section = "NationalIdProtection";

    /// <summary>Versioned or unversioned Key Vault RSA key id (https://vault/keys/name). When set, Key Vault wraps the data keys.</summary>
    public string? KeyVaultKeyId { get; set; }

    /// <summary>Development and tests only: key id to base64 256-bit master key. Refused outside Development.</summary>
    public Dictionary<string, string> LocalKeys { get; set; } = [];

    public string? LocalCurrentKeyId { get; set; }

    public int DataKeyCacheSeconds { get; set; } = 300;
}

/// <summary>A data key as stored: only ever wrapped. <paramref name="WrapKeyId"/> names the wrapping key version used.</summary>
public sealed record WrappedKey(Guid TenantId, string Purpose, string WrapKeyId, byte[] Wrapped);

/// <summary>Where wrapped keys live (a table in part 2 of MDV-P0-035). Add is insert-if-absent and returns the winner.</summary>
public interface IWrappedKeyStore
{
    WrappedKey? Find(Guid tenantId, string purpose);
    WrappedKey Add(WrappedKey key);
    void Replace(WrappedKey key);
}

/// <summary>Dev and test store. Keys die with the process, so never register it where data must survive.</summary>
public sealed class InMemoryWrappedKeyStore : IWrappedKeyStore
{
    private readonly ConcurrentDictionary<(Guid, string), WrappedKey> _keys = new();

    public WrappedKey? Find(Guid tenantId, string purpose) => _keys.GetValueOrDefault((tenantId, purpose));
    public WrappedKey Add(WrappedKey key) => _keys.GetOrAdd((key.TenantId, key.Purpose), key);
    public void Replace(WrappedKey key) => _keys[(key.TenantId, key.Purpose)] = key;
}

/// <summary>Per-tenant keys, wrapped by <see cref="IKeyWrapper"/>. Unwrapped keys live in memory only, for a short time.</summary>
public sealed class TenantKeyRing(IWrappedKeyStore store, IKeyWrapper wrapper, IOptions<NationalIdProtectionOptions> options, TimeProvider clock)
{
    public const string DataPurpose = "data";
    public const string BlindIndexPurpose = "blind-index";

    private readonly ConcurrentDictionary<(Guid, string), (byte[] Key, DateTimeOffset Expires)> _cache = new();

    public byte[] Get(Guid tenantId, string purpose)
    {
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue((tenantId, purpose), out var hit) && hit.Expires > now) return hit.Key;
        var key = Guarded(() =>
        {
            var record = store.Find(tenantId, purpose) ?? store.Add(Create(tenantId, purpose));
            return wrapper.Unwrap(record.WrapKeyId, record.Wrapped);
        });
        _cache[(tenantId, purpose)] = (key, now.AddSeconds(options.Value.DataKeyCacheSeconds));
        return key;
    }

    /// <summary>Moves the tenant's keys to the wrapper's current key version. Ciphertext rows are untouched.</summary>
    public void Rewrap(Guid tenantId)
    {
        foreach (var purpose in new[] { DataPurpose, BlindIndexPurpose })
        {
            Guarded(() =>
            {
                if (store.Find(tenantId, purpose) is { } old)
                {
                    var fresh = wrapper.Wrap(wrapper.Unwrap(old.WrapKeyId, old.Wrapped));
                    store.Replace(old with { WrapKeyId = fresh.KeyId, Wrapped = fresh.Bytes });
                }
                return 0;
            });
            _cache.TryRemove((tenantId, purpose), out _);
        }
    }

    private WrappedKey Create(Guid tenantId, string purpose)
    {
        var wrapped = wrapper.Wrap(RandomNumberGenerator.GetBytes(32));
        return new WrappedKey(tenantId, purpose, wrapped.KeyId, wrapped.Bytes);
    }

    // Fail closed: any wrapper or store failure becomes one typed exception with fixed text.
    private static T Guarded<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception e) when (e is not KeyProtectionException) { throw new KeyProtectionException(KeyProtectionException.KeysUnavailable, e); }
    }
}
