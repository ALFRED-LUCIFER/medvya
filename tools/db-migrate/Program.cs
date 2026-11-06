using Medvya.DbMigrate;
using Npgsql;

// migrate <connection>   module migrations, then the Hangfire tables.
// counts  <connection>   row count per table (counts only, no data read), for the restore drill.
// shortcut: no `admin` step (roles, keys) until MDV-P0-034 (roles) and MDV-P0-035 (key wrapping) are on Postgres.
if (args is not [var command, var connection] || command is not ("migrate" or "counts"))
{
    await Console.Error.WriteLineAsync("usage: db-migrate migrate <connection> | db-migrate counts <connection>");
    return 2;
}

await using var db = new NpgsqlConnection(connection);
await db.OpenAsync();
if (command == "counts")
{
    foreach (var (table, rows) in await DatabaseMigrator.RowCountsAsync(db)) await Console.Out.WriteLineAsync($"{table}\t{rows}");
    return 0;
}

await DatabaseMigrator.ApplyAsync(db);
await Console.Out.WriteLineAsync($"db-migrate {command}: done");
return 0;
