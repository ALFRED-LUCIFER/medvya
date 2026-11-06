using Medvya.BuildingBlocks.Infrastructure;
using Medvya.DbMigrate;
using Npgsql;

// admin   <connection> [medvya_role=password ...]   roles, schemas and privileges (infra/postgres/roles.sql); the administrator's step, run first.
//                                                    The role=password pairs make development logins; in Azure use entra.
// migrate <connection>                              module migrations, then the Hangfire tables. Run as the medvya_migrator login.
// entra   <connection to database postgres> <medvya_role> <principal name> <object id>
//                                                    Azure: makes a managed identity a login IN ROLE medvya_app / _migrator / _reporting.
// counts  <connection>                              row count per table (counts only, no data read), for the restore drill.
// An Azure host without a password in the connection string signs in with an Entra token (PostgresDataSources).
const string Usage = "usage: db-migrate admin <connection> [medvya_role=password ...] | db-migrate migrate <connection> | db-migrate entra <connection> <medvya_role> <principal name> <object id> | db-migrate counts <connection>";
var valid = args switch
{
    ["admin", _, ..] or ["migrate", _] or ["counts", _] => true,
    ["entra", _, _, _, var oid] => Guid.TryParse(oid, out _),
    _ => false,
};
if (!valid)
{
    await Console.Error.WriteLineAsync(Usage);
    return 2;
}
var (command, connection, rest) = (args[0], args[1], args[2..]);

await using var db = await PostgresDataSources.For(connection).OpenConnectionAsync();
if (command == "counts")
{
    foreach (var (table, rows) in await DatabaseMigrator.RowCountsAsync(db)) await Console.Out.WriteLineAsync($"{table}\t{rows}");
    return 0;
}

if (command == "entra")
    await DatabaseMigrator.MapEntraPrincipalAsync(db, rest[0], rest[1], Guid.Parse(rest[2]));
else if (command == "admin")
    await DatabaseMigrator.ApplyRolesAsync(db, rest.Select(p => (p[..p.IndexOf('=')], p[(p.IndexOf('=') + 1)..])));
else
    await DatabaseMigrator.ApplyAsync(db);
await Console.Out.WriteLineAsync($"db-migrate {command}: done");
return 0;

// Internal on purpose: the ASP.NET Core framework (via Medvya.BuildingBlocks.Security) would otherwise make Program public and clash with Host.Api in the tests.
internal partial class Program;
