using Azure.Identity;
using Medvya.DbMigrate;
using Medvya.Patients.Infrastructure;
using Microsoft.Data.SqlClient;

// admin   <connection> <key-vault-key-url>  roles.sql, then the Always Encrypted keys wrapped by the Key Vault key. Entra administrator only.
// migrate <connection>                      module migrations, then the Hangfire tables. The migration identity only.
// Connection strings carry no password: Authentication=Active Directory Default;Encrypt=Strict.
if (args is not ([var command, var connection, ..] and var all) || command is not ("admin" or "migrate") || (command == "admin") != (all.Length == 3))
{
    await Console.Error.WriteLineAsync("usage: db-migrate admin <connection> <key-vault-key-url> | db-migrate migrate <connection>");
    return 2;
}

await using var db = new SqlConnection(connection);
await db.OpenAsync();
if (command == "admin")
{
    await DatabaseMigrator.EnsureRolesAsync(db);
    // DefaultAzureCredential: az login on a workstation, the OIDC login in CI.
    var provider = ColumnEncryption.RegisterKeyVault(new DefaultAzureCredential());
    await ColumnEncryption.EnsureKeysAsync(db, ColumnEncryption.KeyVaultProviderName, all[2], provider);
}
else
{
    await DatabaseMigrator.ApplyAsync(db);
}

await Console.Out.WriteLineAsync($"db-migrate {command}: done");
return 0;
