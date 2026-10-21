using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// Always Encrypted keys for the patients schema (MDV-P0-009). The column master key (CMK) never reaches SQL Server: the database
/// holds only its metadata (provider name + key path) and the column encryption key (CEK) wrapped by it.
/// <para>
/// Order of operations, same shape everywhere: the administrator runs infra/sql/roles.sql, then <see cref="EnsureKeysAsync"/>
/// (needs ALTER ANY COLUMN MASTER/ENCRYPTION KEY and the CMK's wrap right), then the migration principal runs migrations, which
/// create the encrypted columns on <see cref="EncryptionKeyName"/>. Only the provider differs: Azure Key Vault in the cloud,
/// <see cref="LocalKeyStoreProvider"/> (an RSA key held by this process) on developer machines and in tests.
/// </para>
/// </summary>
public static class ColumnEncryption
{
    public const string MasterKeyName = "CMK_Medvya";
    public const string EncryptionKeyName = "CEK_Patients";

    /// <summary>Creates the CMK metadata and a fresh random CEK wrapped by the CMK, unless they exist. Idempotent.</summary>
    public static async Task EnsureKeysAsync(SqlConnection admin, string providerName, string keyPath, SqlColumnEncryptionKeyStoreProvider provider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(provider);
        // DDL takes no parameters; the names are constants and the two settings come from deployment configuration, quoted here.
        static string Quote(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

        var cek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapped = provider.EncryptColumnEncryptionKey(keyPath, "RSA_OAEP", cek);
            await using var cmd = new SqlCommand($"""
                IF NOT EXISTS (SELECT 1 FROM sys.column_master_keys WHERE name = N'{MasterKeyName}')
                    CREATE COLUMN MASTER KEY [{MasterKeyName}] WITH (KEY_STORE_PROVIDER_NAME = {Quote(providerName)}, KEY_PATH = {Quote(keyPath)});
                IF NOT EXISTS (SELECT 1 FROM sys.column_encryption_keys WHERE name = N'{EncryptionKeyName}')
                    CREATE COLUMN ENCRYPTION KEY [{EncryptionKeyName}] WITH VALUES
                        (COLUMN_MASTER_KEY = [{MasterKeyName}], ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = 0x{Convert.ToHexString(wrapped)});
                """, admin);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
        }
    }
}

/// <summary>
/// Column master key held as an RSA key by this process, for developer machines and tests only (the Windows certificate store
/// provider does not exist on macOS/Linux). Production registers the Azure Key Vault provider instead.
/// shortcut: production CMK in Key Vault (Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider, API identity only) arrives with MDV-P0-006.
/// </summary>
public sealed class LocalKeyStoreProvider(RSA key) : SqlColumnEncryptionKeyStoreProvider
{
    public const string ProviderName = "MEDVYA_LOCAL_RSA";
    public const string KeyPath = "local/column-master-key";

    static readonly Lock Gate = new();
    static LocalKeyStoreProvider? registered;

    /// <summary>
    /// Registers the provider for every SqlConnection in this process. SqlClient allows one global registration per process, so a
    /// second call returns the provider already registered (its key wins).
    /// </summary>
    public static LocalKeyStoreProvider Register(Func<RSA> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (Gate)
        {
            if (registered is not null) return registered;
            var provider = new LocalKeyStoreProvider(key());
            SqlConnection.RegisterColumnEncryptionKeyStoreProviders(new Dictionary<string, SqlColumnEncryptionKeyStoreProvider> { [ProviderName] = provider });
            return registered = provider;
        }
    }

    /// <summary>Loads the developer CMK from a PEM file, creating it (owner read/write only) on first use. Synthetic data only.</summary>
    public static RSA LoadOrCreate(string pemPath)
    {
        var rsa = RSA.Create(3072);
        if (File.Exists(pemPath))
        {
            rsa.ImportFromPem(File.ReadAllText(pemPath));
            return rsa;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(pemPath)!);
        File.WriteAllText(pemPath, rsa.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pemPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return rsa;
    }

    public override byte[] EncryptColumnEncryptionKey(string masterKeyPath, string encryptionAlgorithm, byte[] columnEncryptionKey)
    {
        CheckPath(masterKeyPath);
        return key.Encrypt(columnEncryptionKey, RSAEncryptionPadding.OaepSHA256);
    }

    public override byte[] DecryptColumnEncryptionKey(string masterKeyPath, string encryptionAlgorithm, byte[] encryptedColumnEncryptionKey)
    {
        CheckPath(masterKeyPath);
        return key.Decrypt(encryptedColumnEncryptionKey, RSAEncryptionPadding.OaepSHA256);
    }

    static void CheckPath(string masterKeyPath)
    {
        if (masterKeyPath != KeyPath) throw new ArgumentException("Unknown column master key path.", nameof(masterKeyPath));
    }
}
