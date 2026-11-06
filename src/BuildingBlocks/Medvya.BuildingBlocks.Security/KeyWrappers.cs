using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Security;

public readonly record struct WrappedBytes(string KeyId, byte[] Bytes);

/// <summary>Wraps data keys with a key that never leaves the key service. KeyId names the wrapping key version, so Unwrap works after rotation.</summary>
public interface IKeyWrapper
{
    WrappedBytes Wrap(byte[] key);
    byte[] Unwrap(string keyId, byte[] wrapped);
}

/// <summary>Development and tests: AES-256-GCM under configured master keys. Refuses to exist outside Development.</summary>
public sealed class LocalKeyWrapper : IKeyWrapper
{
    private readonly IReadOnlyDictionary<string, byte[]> _masters;
    private readonly string _currentId;

    public LocalKeyWrapper(IHostEnvironment env, IReadOnlyDictionary<string, byte[]> masters, string currentId)
    {
        if (!env.IsDevelopment()) throw new InvalidOperationException("LocalKeyWrapper is for Development only; configure Key Vault.");
        if (!masters.ContainsKey(currentId) || masters.Values.Any(k => k.Length != 32))
            throw new InvalidOperationException("Local master keys must be 256-bit and include the current key id.");
        (_masters, _currentId) = (masters, currentId);
    }

    public LocalKeyWrapper(IHostEnvironment env, IOptions<NationalIdProtectionOptions> options)
        : this(env, options.Value.LocalKeys.ToDictionary(p => p.Key, p => Convert.FromBase64String(p.Value)), options.Value.LocalCurrentKeyId ?? "")
    {
    }

    public const string MachineKeyId = "machine";

    /// <summary>
    /// Development with no configured keys: one random 256-bit master per machine and user, created on first use in local application
    /// data (never in the repository), so the API, the seeder and the tests on one machine read each other's data across restarts.
    /// </summary>
    public static LocalKeyWrapper ForThisMachine(IHostEnvironment env)
    {
        if (!env.IsDevelopment()) throw new InvalidOperationException("LocalKeyWrapper is for Development only; configure Key Vault.");
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "medvya");
        var path = Path.Combine(folder, "national-id-development.key");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(folder);
            var temp = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) create.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // readable by this user only
            using (var f = new FileStream(temp, create)) f.Write(RandomNumberGenerator.GetBytes(32));
            try { File.Move(temp, path, overwrite: false); } // atomic: a parallel first run keeps whichever key landed first
            catch (IOException) when (File.Exists(path)) { File.Delete(temp); }
        }
        return new LocalKeyWrapper(env, new Dictionary<string, byte[]> { [MachineKeyId] = File.ReadAllBytes(path) }, MachineKeyId);
    }

    public WrappedBytes Wrap(byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ct = new byte[key.Length];
        using var aes = new AesGcm(_masters[_currentId], 16);
        aes.Encrypt(nonce, key, ct, tag);
        return new(_currentId, [.. nonce, .. ct, .. tag]);
    }

    public byte[] Unwrap(string keyId, byte[] wrapped)
    {
        if (!_masters.TryGetValue(keyId, out var master)) throw new KeyProtectionException(KeyProtectionException.KeysUnavailable);
        var pt = new byte[wrapped.Length - 28];
        using var aes = new AesGcm(master, 16);
        aes.Decrypt(wrapped.AsSpan(0, 12), wrapped.AsSpan(12, pt.Length), wrapped.AsSpan(12 + pt.Length), pt);
        return pt;
    }
}

/// <summary>Key Vault RSA-OAEP-256 wrap. The client factory takes a versioned key id, or null for the current version.</summary>
public sealed class AzureKeyVaultKeyWrapper(Func<string?, CryptographyClient> client) : IKeyWrapper
{
    public WrappedBytes Wrap(byte[] key)
    {
        var r = client(null).WrapKey(KeyWrapAlgorithm.RsaOaep256, key);
        return new(r.KeyId, r.EncryptedKey);
    }

    public byte[] Unwrap(string keyId, byte[] wrapped) => client(keyId).UnwrapKey(KeyWrapAlgorithm.RsaOaep256, wrapped).Key;
}
