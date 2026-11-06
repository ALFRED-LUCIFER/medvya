using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Security;

/// <summary>Application-level encryption of national and insurance IDs (the NationalId data class).</summary>
public interface INationalIdProtector
{
    byte[] Protect(Guid tenantId, string plaintext);
    string Unprotect(Guid tenantId, byte[] protectedValue);
}

public interface IBlindIndex
{
    /// <summary>Exact-match lookup key. Pass the value through <see cref="BlindIndex.Normalise"/> first.</summary>
    byte[] Compute(Guid tenantId, string normalisedValue);
}

/// <summary>Value layout: version(1) | nonce(12) | ciphertext | tag(16), AES-256-GCM, associated data = tenant id + purpose.</summary>
public sealed class NationalIdProtector(TenantKeyRing keys) : INationalIdProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12, TagSize = 16;
    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("national-id:v1");

    // shortcut: one data key per tenant (re-wrapped, never re-keyed); add a key generation after the version byte when data must be re-keyed.
    public byte[] Protect(Guid tenantId, string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[1 + NonceSize + pt.Length + TagSize];
        output[0] = Version;
        RandomNumberGenerator.Fill(output.AsSpan(1, NonceSize));
        using var aes = new AesGcm(keys.Get(tenantId, TenantKeyRing.DataPurpose), TagSize);
        aes.Encrypt(output.AsSpan(1, NonceSize), pt, output.AsSpan(1 + NonceSize, pt.Length), output.AsSpan(1 + NonceSize + pt.Length), AssociatedData(tenantId));
        return output;
    }

    public string Unprotect(Guid tenantId, byte[] protectedValue)
    {
        if (protectedValue is not { Length: > 1 + NonceSize + TagSize } || protectedValue[0] != Version)
            throw new KeyProtectionException(KeyProtectionException.UnreadableValue);
        var pt = new byte[protectedValue.Length - 1 - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(keys.Get(tenantId, TenantKeyRing.DataPurpose), TagSize);
            aes.Decrypt(protectedValue.AsSpan(1, NonceSize), protectedValue.AsSpan(1 + NonceSize, pt.Length), protectedValue.AsSpan(1 + NonceSize + pt.Length), pt, AssociatedData(tenantId));
        }
        catch (CryptographicException) { throw new KeyProtectionException(KeyProtectionException.UnreadableValue); }
        return Encoding.UTF8.GetString(pt);
    }

    private static byte[] AssociatedData(Guid tenantId) => [.. tenantId.ToByteArray(), .. Purpose];
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
    /// <summary>Registers the protector and blind index. The caller registers an <see cref="IWrappedKeyStore"/> (a table, from part 2).</summary>
    public static IServiceCollection AddNationalIdProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NationalIdProtectionOptions>().Bind(configuration.GetSection(NationalIdProtectionOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IKeyWrapper>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<NationalIdProtectionOptions>>();
            if (string.IsNullOrEmpty(options.Value.KeyVaultKeyId))
                return new LocalKeyWrapper(sp.GetRequiredService<IHostEnvironment>(), options);
            var credential = new DefaultAzureCredential();
            var clients = new ConcurrentDictionary<string, CryptographyClient>();
            return new AzureKeyVaultKeyWrapper(id => clients.GetOrAdd(id ?? options.Value.KeyVaultKeyId, k => new CryptographyClient(new Uri(k), credential)));
        });
        services.AddSingleton<TenantKeyRing>();
        services.AddSingleton<INationalIdProtector, NationalIdProtector>();
        services.AddSingleton<IBlindIndex, BlindIndex>();
        return services;
    }
}

public static class NationalIdValueConverter
{
    /// <summary>EF converter for a NationalId-class string column. Not applied to any entity yet (part 2).</summary>
    public static ValueConverter<string, byte[]> Create(INationalIdProtector protector, Func<Guid?> tenantId) =>
        new(v => protector.Protect(RequireTenant(tenantId), v), b => protector.Unprotect(RequireTenant(tenantId), b));

    public static Guid RequireTenant(Func<Guid?> tenantId) => tenantId() ?? throw new KeyProtectionException(KeyProtectionException.NoTenant);
}
