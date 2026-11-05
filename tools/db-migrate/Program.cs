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
// The serverless database auto-pauses; the first connection wakes it and fails with 40613 until it is online (about a minute).
for (var attempt = 1; ; attempt++)
{
    try { await db.OpenAsync(); break; }
    catch (SqlException e) when (e.Number == 40613 && attempt < 8) { await Task.Delay(TimeSpan.FromSeconds(15)); }
}
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
