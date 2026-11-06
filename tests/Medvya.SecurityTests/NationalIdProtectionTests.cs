using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Medvya.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Medvya.SecurityTests;

public sealed class NationalIdProtectionTests
{
    // Synthetic and invalid by construction: fail Verhoeff and Luhn (tools/check-synthetic).
    private const string Aadhaar = "2345 6789 0123";
    private const string EmiratesId = "784-1988-1234567-1";

    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static Dictionary<string, byte[]> Masters(params string[] ids) => ids.ToDictionary(i => i, _ => RandomNumberGenerator.GetBytes(32));

    private sealed class Rig
    {
        public LocalKeyWrapper Wrapper;
        public TenantKeyRing Ring;
        public NationalIdProtector Protector;
        public BlindIndex Index;

        public Rig(IKeyWrapper? wrapper = null, IWrappedKeyStore? store = null, TimeProvider? clock = null)
        {
            Wrapper = new LocalKeyWrapper(new Env("Development"), Masters("v1"), "v1");
            Ring = new TenantKeyRing(store ?? new InMemoryWrappedKeyStore(), wrapper ?? Wrapper, Options.Create(new NationalIdProtectionOptions { DataKeyCacheSeconds = 60 }), clock ?? TimeProvider.System);
            Protector = new NationalIdProtector(Ring);
            Index = new BlindIndex(Ring);
        }
    }

    [Fact]
    public void RoundTripsAndIsNotPlaintext()
    {
        var r = new Rig();
        var value = r.Protector.Protect(TenantA, Aadhaar);
        Assert.Equal(Aadhaar, r.Protector.Unprotect(TenantA, value));
        Assert.False(value.AsSpan().IndexOf("2345"u8) >= 0);
    }

    [Fact]
    public void TamperingWithAnyByteFails()
    {
        var r = new Rig();
        var value = r.Protector.Protect(TenantA, EmiratesId);
        for (var i = 0; i < value.Length; i++)
        {
            var bad = (byte[])value.Clone();
            bad[i] ^= 0x01;
            Assert.Throws<KeyProtectionException>(() => r.Protector.Unprotect(TenantA, bad));
        }
    }

    [Fact]
    public void ValueFromOneTenantDoesNotDecryptUnderAnother()
    {
        var r = new Rig();
        var value = r.Protector.Protect(TenantA, EmiratesId);
        Assert.Throws<KeyProtectionException>(() => r.Protector.Unprotect(TenantB, value));
    }

    [Fact]
    public void AssociatedDataAloneBlocksCrossTenantReads()
    {
        // Same data key for both tenants: only the tenant id in the associated data can stop the read.
        var shared = new SharedKeyStore();
        var a = new Rig(store: shared);
        var value = a.Protector.Protect(TenantA, EmiratesId);
        Assert.Throws<KeyProtectionException>(() => a.Protector.Unprotect(TenantB, value));
    }

    private sealed class SharedKeyStore : IWrappedKeyStore
    {
        private readonly InMemoryWrappedKeyStore _inner = new();
        public WrappedKey? Find(Guid tenantId, string purpose) => _inner.Find(Guid.Empty, purpose) is { } k ? k with { TenantId = tenantId } : null;
        public WrappedKey Add(WrappedKey key) => _inner.Add(key with { TenantId = Guid.Empty }) with { TenantId = key.TenantId };
        public void Replace(WrappedKey key) => _inner.Replace(key with { TenantId = Guid.Empty });
    }

    [Fact]
    public void NonceIsUniqueOver10000Calls()
    {
        var r = new Rig();
        var nonces = Enumerable.Range(0, 10_000).Select(_ => Convert.ToHexString(r.Protector.Protect(TenantA, Aadhaar).AsSpan(1, 12))).ToHashSet();
        Assert.Equal(10_000, nonces.Count);
    }

    [Fact]
    public void SamePlaintextGivesDifferentCiphertextButTheSameBlindIndexWhichDiffersPerTenant()
    {
        var r = new Rig();
        Assert.NotEqual(r.Protector.Protect(TenantA, Aadhaar), r.Protector.Protect(TenantA, Aadhaar));
        var n = BlindIndex.Normalise(Aadhaar);
        Assert.Equal(r.Index.Compute(TenantA, n), r.Index.Compute(TenantA, n));
        Assert.NotEqual(r.Index.Compute(TenantA, n), r.Index.Compute(TenantB, n));
        Assert.Equal(32, r.Index.Compute(TenantA, n).Length);
    }

    [Theory]
    [InlineData(" 2345 6789 0123 ", "234567890123")]
    [InlineData("2345-6789-0123", "234567890123")]
    [InlineData("784-1988-1234567-1", "784198812345671")]
    [InlineData("784 1988 1234567 1", "784198812345671")]
    [InlineData("ab-12 cd", "AB12CD")]
    public void NormalisationRule(string input, string expected) => Assert.Equal(expected, BlindIndex.Normalise(input));

    [Fact]
    public void RewrapMovesKeysToTheNewMasterAndOldValuesStillDecrypt()
    {
        var store = new InMemoryWrappedKeyStore();
        var masters = Masters("v1", "v2");
        var env = new Env("Development");
        var before = new Rig(new LocalKeyWrapper(env, masters, "v1"), store);
        var value = before.Protector.Protect(TenantA, EmiratesId);
        var index = before.Index.Compute(TenantA, "X");
        Assert.Equal("v1", store.Find(TenantA, TenantKeyRing.DataPurpose)!.WrapKeyId);

        var after = new Rig(new LocalKeyWrapper(env, masters, "v2"), store);
        after.Ring.Rewrap(TenantA);

        Assert.Equal("v2", store.Find(TenantA, TenantKeyRing.DataPurpose)!.WrapKeyId);
        Assert.Equal("v2", store.Find(TenantA, TenantKeyRing.BlindIndexPurpose)!.WrapKeyId);
        Assert.Equal(EmiratesId, after.Protector.Unprotect(TenantA, value));
        Assert.Equal(index, after.Index.Compute(TenantA, "X"));
        // Retiring v1 now breaks nothing, which proves the stored keys no longer depend on it.
        var onlyV2 = new Rig(new LocalKeyWrapper(env, new Dictionary<string, byte[]> { ["v2"] = masters["v2"] }, "v2"), store);
        Assert.Equal(EmiratesId, onlyV2.Protector.Unprotect(TenantA, value));
    }

    [Fact]
    public void UnwrappedKeysAreCachedOnlyBriefly()
    {
        var clock = new FakeClock();
        var counting = new CountingWrapper();
        var r = new Rig(counting, clock: clock);
        r.Protector.Protect(TenantA, Aadhaar);
        r.Protector.Protect(TenantA, Aadhaar);
        Assert.Equal(1, counting.Unwraps);
        clock.Now = clock.Now.AddSeconds(61);
        r.Protector.Protect(TenantA, Aadhaar);
        Assert.Equal(2, counting.Unwraps);
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingWrapper : IKeyWrapper
    {
        private readonly LocalKeyWrapper _inner = new(new Env("Development"), Masters("v1"), "v1");
        public int Unwraps;
        public WrappedBytes Wrap(byte[] key) => _inner.Wrap(key);
        public byte[] Unwrap(string keyId, byte[] wrapped) { Unwraps++; return _inner.Unwrap(keyId, wrapped); }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void LocalWrapperRefusesToStartOutsideDevelopment(string environment) =>
        Assert.Throws<InvalidOperationException>(() => new LocalKeyWrapper(new Env(environment), Masters("v1"), "v1"));

    [Fact]
    public void DiOutsideDevelopmentWithoutKeyVaultFailsClosed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Env("Production"));
        services.AddSingleton<IWrappedKeyStore, InMemoryWrappedKeyStore>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddNationalIdProtection();
        using var sp = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<INationalIdProtector>());
    }

    [Fact]
    public void TheMachineKeyIsTheSameAcrossRestartsAndRefusedOutsideDevelopment()
    {
        var wrapped = LocalKeyWrapper.ForThisMachine(new Env("Development")).Wrap(new byte[32]);
        Assert.Equal(LocalKeyWrapper.MachineKeyId, wrapped.KeyId);
        Assert.Equal(new byte[32], LocalKeyWrapper.ForThisMachine(new Env("Development")).Unwrap(wrapped.KeyId, wrapped.Bytes)); // a second process
        Assert.Throws<InvalidOperationException>(() => LocalKeyWrapper.ForThisMachine(new Env("Production")));
    }

    [Fact]
    public void DiInDevelopmentWithLocalKeysRoundTrips()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NationalIdProtection:LocalCurrentKeyId"] = "v1",
            ["NationalIdProtection:LocalKeys:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Env("Development"));
        services.AddSingleton<IWrappedKeyStore, InMemoryWrappedKeyStore>();
        services.AddSingleton<IConfiguration>(config);
        services.AddNationalIdProtection();
        using var sp = services.BuildServiceProvider();
        var p = sp.GetRequiredService<INationalIdProtector>();
        Assert.Equal(Aadhaar, p.Unprotect(TenantA, p.Protect(TenantA, Aadhaar)));
    }

    // Key Vault seam: a CryptographyClient subclass, no network.
    private sealed class FakeCryptographyClient(string keyId, bool fail = false) : CryptographyClient
    {
        public override WrapResult WrapKey(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken cancellationToken = default) =>
            fail ? throw new Azure.RequestFailedException("vault down")
                 : CryptographyModelFactory.WrapResult(keyId, key.Reverse().ToArray(), algorithm);

        public override UnwrapResult UnwrapKey(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken cancellationToken = default) =>
            fail ? throw new Azure.RequestFailedException("vault down")
                 : CryptographyModelFactory.UnwrapResult(keyId, encryptedKey.Reverse().ToArray(), algorithm);
    }

    [Fact]
    public void KeyVaultWrapperRoundTripsAndRecordsTheKeyVersion()
    {
        var w = new AzureKeyVaultKeyWrapper(id => new FakeCryptographyClient(id ?? "https://vault.test/keys/k/v7"));
        var wrapped = w.Wrap([1, 2, 3, 4]);
        Assert.Equal("https://vault.test/keys/k/v7", wrapped.KeyId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, w.Unwrap(wrapped.KeyId, wrapped.Bytes));
        var r = new Rig(w);
        Assert.Equal(Aadhaar, r.Protector.Unprotect(TenantA, r.Protector.Protect(TenantA, Aadhaar)));
    }

    [Fact]
    public void KeyVaultOutageFailsClosedWithATypedExceptionThatLeaksNothing()
    {
        var r = new Rig(new AzureKeyVaultKeyWrapper(_ => new FakeCryptographyClient("k", fail: true)));
        var ex = Assert.Throws<KeyProtectionException>(() => r.Protector.Protect(TenantA, Aadhaar));
        Assert.Equal(KeyProtectionException.KeysUnavailable, ex.Message);
        Assert.DoesNotContain("2345", ex.Message);
        Assert.DoesNotContain(TenantA.ToString(), ex.Message);
    }

    [Fact]
    public void ExceptionsNeverContainTheValueOrTenant()
    {
        var r = new Rig();
        var good = r.Protector.Protect(TenantA, EmiratesId);
        var bad = (byte[])good.Clone();
        bad[^1] ^= 1;
        var texts = new[]
        {
            Assert.Throws<KeyProtectionException>(() => r.Protector.Unprotect(TenantA, bad)).ToString(),
            Assert.Throws<KeyProtectionException>(() => r.Protector.Unprotect(TenantB, good)).ToString(),
            Assert.Throws<KeyProtectionException>(() => r.Protector.Unprotect(TenantA, [1, 2, 3])).ToString(),
            Assert.Throws<ArgumentException>(() => r.Protector.Protect(TenantA, "")).ToString(),
            Assert.Throws<KeyProtectionException>(() => NationalIdValueConverter.RequireTenant(() => null)).ToString(),
        };
        foreach (var t in texts)
        {
            Assert.DoesNotContain("1988", t);
            Assert.DoesNotContain(TenantA.ToString(), t);
            Assert.DoesNotContain(TenantB.ToString(), t);
            Assert.DoesNotContain(Convert.ToHexString(good), t, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EfConverterUsesTheTenantAccessor()
    {
        var r = new Rig();
        Guid? tenant = TenantA;
        var c = NationalIdValueConverter.Create(r.Protector, () => tenant);
        var stored = (byte[])c.ConvertToProvider(Aadhaar)!;
        Assert.Equal(Aadhaar, c.ConvertFromProvider(stored));
        tenant = TenantB;
        Assert.Throws<KeyProtectionException>(() => c.ConvertFromProvider(stored));
        tenant = null;
        Assert.Throws<KeyProtectionException>(() => c.ConvertToProvider(Aadhaar));
    }
}
