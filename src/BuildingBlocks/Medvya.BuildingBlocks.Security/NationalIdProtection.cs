using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Security;

/// <summary>Application-level encryption of national and insurance IDs (the NationalId data class).</summary>
public interface INationalIdProtector
{
    /// <summary><paramref name="binding"/> names the one place the value may live (column and row id): a copy pasted into another row or column does not decrypt.</summary>
    byte[] Protect(Guid tenantId, string plaintext, string binding);
    string Unprotect(Guid tenantId, byte[] protectedValue, string binding);
}

public interface IBlindIndex
{
    /// <summary>Exact-match lookup key. Pass the value through <see cref="BlindIndex.Normalise"/> first.</summary>
    byte[] Compute(Guid tenantId, string normalisedValue);
}

/// <summary>Value layout: version(1) | nonce(12) | ciphertext | tag(16), AES-256-GCM, associated data = tenant id + purpose + binding.</summary>
public sealed class NationalIdProtector(TenantKeyRing keys) : INationalIdProtector
{
    // Version 2 binds each value to its column and row (MDV-P0-041). Version 1 (tenant only) is refused: it only ever held synthetic data.
    private const byte Version = 2;
    private const int NonceSize = 12, TagSize = 16;
    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("national-id:v2|");

    // shortcut: one data key per tenant (re-wrapped, never re-keyed); add a key generation after the version byte when data must be re-keyed.
    public byte[] Protect(Guid tenantId, string plaintext, string binding)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(binding);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[1 + NonceSize + pt.Length + TagSize];
        output[0] = Version;
        RandomNumberGenerator.Fill(output.AsSpan(1, NonceSize));
        using var aes = new AesGcm(keys.Get(tenantId, TenantKeyRing.DataPurpose), TagSize);
        aes.Encrypt(output.AsSpan(1, NonceSize), pt, output.AsSpan(1 + NonceSize, pt.Length), output.AsSpan(1 + NonceSize + pt.Length), AssociatedData(tenantId, binding));
        return output;
    }

    public string Unprotect(Guid tenantId, byte[] protectedValue, string binding)
    {
        if (protectedValue is not { Length: > 1 + NonceSize + TagSize } || protectedValue[0] != Version)
            throw new KeyProtectionException(KeyProtectionException.UnreadableValue);
        var pt = new byte[protectedValue.Length - 1 - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(keys.Get(tenantId, TenantKeyRing.DataPurpose), TagSize);
            aes.Decrypt(protectedValue.AsSpan(1, NonceSize), protectedValue.AsSpan(1 + NonceSize, pt.Length), protectedValue.AsSpan(1 + NonceSize + pt.Length), pt, AssociatedData(tenantId, binding));
        }
        catch (CryptographicException) { throw new KeyProtectionException(KeyProtectionException.UnreadableValue); }
        return Encoding.UTF8.GetString(pt);
    }

    // The tenant id is fixed-length and the purpose ends in a separator, so the concatenation is unambiguous.
    private static byte[] AssociatedData(Guid tenantId, string binding) => [.. tenantId.ToByteArray(), .. Purpose, .. Encoding.UTF8.GetBytes(binding)];
}

/// <summary>HMAC-SHA-256 under a per-tenant secret kept separate from the data key, so equal IDs in two tenants never match.</summary>
public sealed class BlindIndex(TenantKeyRing keys) : IBlindIndex
{
    /// <summary>Normalisation rule: trim, drop spaces and hyphens, upper-case. "784-1988-1234567-1" and "7841988 1234567 1" index the same.</summary>
    public static string Normalise(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(value.Trim().Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();
    }

    // shortcut: the blind-index secret is wrapped by the same Key Vault key as the data key, not a second vault key; split when the review asks for separate key custody.
    // The tenant id is fixed-length (16 bytes), so the concatenation is unambiguous.
    public byte[] Compute(Guid tenantId, string normalisedValue) =>
        HMACSHA256.HashData(keys.Get(tenantId, TenantKeyRing.BlindIndexPurpose), (byte[])[.. tenantId.ToByteArray(), .. Encoding.UTF8.GetBytes(normalisedValue)]);
}

public static class NationalIdProtectionExtensions
{
    /// <summary>
    /// Registers the protector and blind index, configured from the NationalIdProtection section. The caller registers an
    /// <see cref="IWrappedKeyStore"/> (the Patients module keeps it in patients."DataKeys"). Development without Key Vault uses the configured
    /// local keys, or else this machine's key (<see cref="LocalKeyWrapper.ForThisMachine"/>); anywhere else without Key Vault it fails closed.
    /// </summary>
    public static IServiceCollection AddNationalIdProtection(this IServiceCollection services)
    {
        services.AddOptions<NationalIdProtectionOptions>().BindConfiguration(NationalIdProtectionOptions.Section);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IKeyWrapper>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NationalIdProtectionOptions>>();
            if (string.IsNullOrEmpty(options.Value.KeyVaultKeyId))
                return options.Value.LocalKeys.Count == 0
                    ? LocalKeyWrapper.ForThisMachine(sp.GetRequiredService<IHostEnvironment>())
                    : new LocalKeyWrapper(sp.GetRequiredService<IHostEnvironment>(), options);
            var credential = new DefaultAzureCredential();
            var clients = new ConcurrentDictionary<string, CryptographyClient>();
            return new AzureKeyVaultKeyWrapper(id => clients.GetOrAdd(id ?? options.Value.KeyVaultKeyId, k => new CryptographyClient(new Uri(k), credential)));
        });
        services.AddHostedService<KeyVaultWarmup>();
        services.AddSingleton<TenantKeyRing>();
        services.AddSingleton<INationalIdProtector, NationalIdProtector>();
        services.AddSingleton<IBlindIndex, BlindIndex>();
        return services;
    }
}
