using Medvya.DbMigrate;
using Npgsql;

// admin   <connection> [medvya_role=password ...]   roles, schemas and privileges (infra/postgres/roles.sql); the administrator's step, run first.
//                                                    The role=password pairs make development logins; in Azure map an Entra identity IN ROLE by hand.
// migrate <connection>                              module migrations, then the Hangfire tables. Run as the medvya_migrator login.
// counts  <connection>                              row count per table (counts only, no data read), for the restore drill.
// shortcut: no key-wrapping step until MDV-P0-035 (envelope encryption) is on Postgres.
if (args is not [var command, var connection, ..var rest] || command is not ("admin" or "migrate" or "counts") || (rest.Length > 0 && command != "admin"))
{
    await Console.Error.WriteLineAsync("usage: db-migrate admin <connection> [medvya_role=password ...] | db-migrate migrate <connection> | db-migrate counts <connection>");
    return 2;
}

await using var db = new NpgsqlConnection(connection);
await db.OpenAsync();
if (command == "counts")
{
    foreach (var (table, rows) in await DatabaseMigrator.RowCountsAsync(db)) await Console.Out.WriteLineAsync($"{table}\t{rows}");
    return 0;
}

if (command == "admin")
    await DatabaseMigrator.ApplyRolesAsync(db, rest.Select(p => (p[..p.IndexOf('=')], p[(p.IndexOf('=') + 1)..])));
else
    await DatabaseMigrator.ApplyAsync(db);
await Console.Out.WriteLineAsync($"db-migrate {command}: done");
return 0;
