using Azure.Identity;
using Medvya.DbMigrate;
using Medvya.Patients.Infrastructure;
using Microsoft.Data.SqlClient;

// admin   <connection> <key-vault-key-url> [<app-identity> <migration-identity>]  roles.sql, the Always Encrypted keys wrapped by the Key Vault
//         key, then (when given) the Entra identities mapped to medvya_app / medvya_migrator, by display name. Entra administrator only.
// migrate <connection>                      module migrations, then the Hangfire tables. The migration identity only.
// counts  <connection>                      row count per table (metadata only, no data read), for the restore drill.
// Connection strings carry no password: Authentication=Active Directory Default;Encrypt=Strict.
if (args is not ([var command, var connection, ..] and var all) || command is not ("admin" or "migrate" or "counts") || (command == "admin") != (all.Length is 3 or 5))
{
    await Console.Error.WriteLineAsync("usage: db-migrate admin <connection> <key-vault-key-url> [<app-identity> <migration-identity>] | db-migrate migrate <connection> | db-migrate counts <connection>");
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
    if (all.Length == 5) await DatabaseMigrator.MapIdentitiesAsync(db, [(all[3], "medvya_app"), (all[4], "medvya_migrator")]);
}
else if (command == "counts")
{
    foreach (var (table, rows) in await DatabaseMigrator.RowCountsAsync(db)) await Console.Out.WriteLineAsync($"{table}\t{rows}");
    return 0;
}
else
{
    await DatabaseMigrator.ApplyAsync(db);
}

await Console.Out.WriteLineAsync($"db-migrate {command}: done");
return 0;
