using System.Reflection;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Security;
using Medvya.Patients.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Medvya.Patients.Infrastructure.Persistence;

/// <summary>A tenant's data key as stored (MDV-P0-035): only ever wrapped by the Key Vault key (or this machine's key in Development).</summary>
public sealed class DataKey
{
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public string Purpose { get; init; } = "";
    [Classification(DataClass.Internal)] public string WrapKeyId { get; set; } = "";
    [Classification(DataClass.Internal)] public byte[] Wrapped { get; set; } = [];
}

/// <summary>
/// Wrapped data keys in patients."DataKeys", one short connection per call inside the key's own tenant, so row-level security applies.
/// Calls are rare: <see cref="TenantKeyRing"/> caches unwrapped keys in memory.
/// </summary>
public sealed class PatientsDataKeyStore(string connectionString) : IWrappedKeyStore
{
    NpgsqlConnection Open(Guid tenantId)
    {
        var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        using var set = new NpgsqlCommand("SELECT set_config('medvya.tenant_id', @t, false)", conn);
        set.Parameters.AddWithValue("t", tenantId.ToString());
        set.ExecuteNonQuery();
        return conn;
    }

    public WrappedKey? Find(Guid tenantId, string purpose)
    {
        using var conn = Open(tenantId);
        using var cmd = new NpgsqlCommand("""SELECT "WrapKeyId", "Wrapped" FROM patients."DataKeys" WHERE "TenantId" = @t AND "Purpose" = @p""", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("p", purpose);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new WrappedKey(tenantId, purpose, r.GetString(0), (byte[])r[1]) : null;
    }

    public WrappedKey Add(WrappedKey key)
    {
        using (var conn = Open(key.TenantId))
        using (var cmd = new NpgsqlCommand("""INSERT INTO patients."DataKeys" ("TenantId", "Purpose", "WrapKeyId", "Wrapped") VALUES (@t, @p, @k, @w) ON CONFLICT DO NOTHING""", conn))
        {
            cmd.Parameters.AddWithValue("t", key.TenantId);
            cmd.Parameters.AddWithValue("p", key.Purpose);
            cmd.Parameters.AddWithValue("k", key.WrapKeyId);
            cmd.Parameters.AddWithValue("w", key.Wrapped);
            cmd.ExecuteNonQuery();
        }
        return Find(key.TenantId, key.Purpose)!; // a parallel first use may have won; everyone uses the winner
    }

    public void Replace(WrappedKey key)
    {
        using var conn = Open(key.TenantId);
        using var cmd = new NpgsqlCommand("""UPDATE patients."DataKeys" SET "WrapKeyId" = @k, "Wrapped" = @w WHERE "TenantId" = @t AND "Purpose" = @p""", conn);
        cmd.Parameters.AddWithValue("t", key.TenantId);
        cmd.Parameters.AddWithValue("p", key.Purpose);
        cmd.Parameters.AddWithValue("k", key.WrapKeyId);
        cmd.Parameters.AddWithValue("w", key.Wrapped);
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// National and insurance IDs in the Patients tables (MDV-P0-035). The domain keeps plain string properties; the database holds only
/// AES-256-GCM ciphertext (bytea, same column names) plus an HMAC blind index for exact-match lookups. Values are encrypted when a row is
/// inserted and decrypted when it is materialised, each with the row's own TenantId: an EF value converter cannot do this, because EF caches
/// one model per context type and a converter would keep the first context's tenant. The values are never queryable in SQL; compare
/// <see cref="Index"/> against the "LookupIndex" column instead. Rows are insert-only for these values (the properties are init-only).
/// </summary>
public sealed class NationalIdColumns(INationalIdProtector protector, IBlindIndex blindIndex) : ISaveChangesInterceptor
{
    /// <summary>
    /// One shared instance: EF keys its internal service provider on materialization interceptors, so a per-context instance would build a
    /// provider per instance. It decrypts with the <see cref="NationalIdColumns"/> of the context that loads the row.
    /// </summary>
    internal static readonly IMaterializationInterceptor DecryptOnLoad = new Materializer();

    public const string LookupCipher = "LookupCipher", DisplayCipher = "DisplayCipher", LookupIndex = "LookupIndex", MemberNumberCipher = "MemberNumberCipher";

    static readonly PropertyInfo Lookup = typeof(PatientIdentifier).GetProperty(nameof(PatientIdentifier.LookupValue))!;
    static readonly PropertyInfo Display = typeof(PatientIdentifier).GetProperty(nameof(PatientIdentifier.DisplayValue))!;
    static readonly PropertyInfo Member = typeof(PayerCoverage).GetProperty(nameof(PayerCoverage.MemberNumber))!;

    /// <summary>For <c>dotnet ef</c> and db-migrate, which never read or write a value: any use is refused.</summary>
    public static NationalIdColumns MigrationsOnly { get; } = new(new Refused(), new Refused());

    /// <summary>The exact-match key for an identifier value in a tenant (normalised: spaces and hyphens dropped, upper case).</summary>
    public byte[] Index(Guid tenantId, string value) => blindIndex.Compute(tenantId, BlindIndex.Normalise(value));

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Protect(eventData.Context!);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Protect(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    void Protect(DbContext db)
    {
        foreach (var e in db.ChangeTracker.Entries<PatientIdentifier>().Where(e => e.State == EntityState.Added))
        {
            var i = e.Entity;
            e.Property(LookupCipher).CurrentValue = Encrypt(i.TenantId, i.LookupValue);
            e.Property(LookupIndex).CurrentValue = i.LookupValue is null ? null : Index(i.TenantId, i.LookupValue);
            e.Property(DisplayCipher).CurrentValue = Encrypt(i.TenantId, i.DisplayValue);
        }
        foreach (var e in db.ChangeTracker.Entries<PayerCoverage>().Where(e => e.State == EntityState.Added))
            e.Property(MemberNumberCipher).CurrentValue = Encrypt(e.Entity.TenantId, e.Entity.MemberNumber);
    }

    sealed class Materializer : IMaterializationInterceptor
    {
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity) =>
            ((PatientsDbContext)materializationData.Context).NationalIds.Decrypt(materializationData, entity);
    }

    object Decrypt(MaterializationInterceptionData materializationData, object entity)
    {
        switch (entity)
        {
            case PatientIdentifier i:
                Lookup.SetValue(i, Decrypt(i.TenantId, materializationData.GetPropertyValue<byte[]?>(LookupCipher)));
                Display.SetValue(i, Decrypt(i.TenantId, materializationData.GetPropertyValue<byte[]?>(DisplayCipher)));
                break;
            case PayerCoverage p:
                Member.SetValue(p, Decrypt(p.TenantId, materializationData.GetPropertyValue<byte[]?>(MemberNumberCipher)));
                break;
        }
        return entity;
    }

    /// <summary>The ciphertext EF would store for this tenant; the app always writes through EF, this is for bulk loads (tests).</summary>
    public byte[]? Encrypt(Guid tenantId, string? value) => value is null ? null : protector.Protect(tenantId, value);

    string? Decrypt(Guid tenantId, byte[]? value) => value is null ? null : protector.Unprotect(tenantId, value);

    sealed class Refused : INationalIdProtector, IBlindIndex
    {
        public byte[] Protect(Guid tenantId, string plaintext) => throw new KeyProtectionException(KeyProtectionException.KeysUnavailable);
        public string Unprotect(Guid tenantId, byte[] protectedValue) => throw new KeyProtectionException(KeyProtectionException.KeysUnavailable);
        public byte[] Compute(Guid tenantId, string normalisedValue) => throw new KeyProtectionException(KeyProtectionException.KeysUnavailable);
    }
}
