using Medvya.SyntheticData;

// Usage: dotnet run --project tools/synthetic-data-generator [-- --uae]
//        dotnet run --project tools/synthetic-data-generator -- patients [--practice "Load Test Clinic"] [--count 40000] [--mrn-start 100000]
//                                                                         [--member-user <staff user id>] [--region centralindia] [--reset] [--doctors 3]
//        dotnet run --project tools/synthetic-data-generator -- practice [--practice "Menon Multi-Doctor Clinic"] [--count 50000] [--mrn-start 100000] [--member-user <id>]
// practice: patients as below plus doctors, nurses, receptionists, an admin, rooms, weekly hours and a few visits (no Entra accounts).
// patients: seeds one named synthetic practice (docs/05-delivery/load-testing.md). --reset clears that practice's patients first; the application
// role may not DELETE, so give ConnectionStrings__MedvyaOwner (a medvya_migrator login, which does SET LOCAL ROLE medvya_owner), or run the whole
// command as a migrator with "Options=-c role=medvya_owner" in ConnectionStrings__Medvya (row-level security still binds the owner).
// patients --doctors n also makes the practice bookable for the desk load test: n doctors with weekly hours, Approved, current terms accepted; prints their calendar ids.
// Needs DOTNET_ENVIRONMENT (Development, Staging or Test) and ConnectionStrings__Medvya; `pnpm dev:db` sets both locally.
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Medvya");
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("ConnectionStrings__Medvya is not set.");
    return 1;
}

SyntheticSeed.EnsureNonProduction(environment);

if (args is ["patients", ..])
{
    string Opt(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
    var practice = Opt("--practice", SyntheticPatients.DefaultPractice);
    var count = int.Parse(Opt("--count", "40000"), System.Globalization.CultureInfo.InvariantCulture);
    var mrnStart = int.Parse(Opt("--mrn-start", "100000"), System.Globalization.CultureInfo.InvariantCulture);
    var member = Opt("--member-user", "") is { Length: > 0 } m ? Guid.Parse(m) : (Guid?)null;
    var owner = args.Contains("--reset") ? Environment.GetEnvironmentVariable("ConnectionStrings__MedvyaOwner") ?? connectionString : null;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var (tenant, facility, _, added) = await SyntheticPatients.SeedAsync(connectionString, environment, practice, count, mrnStart, owner, member, Opt("--region", "centralindia"));
    var doctors = int.Parse(Opt("--doctors", "0"), System.Globalization.CultureInfo.InvariantCulture);
    var (calendars, visits) = doctors > 0 ? await SyntheticPractice.BookableAsync(connectionString, tenant, facility, doctors, count, mrnStart) : ([], 0);
    await Console.Out.WriteLineAsync($"Practice '{practice}' tenant {tenant} facility {facility}: {added} patient(s) added of {count} (MRN {mrnStart} to {mrnStart + count - 1}) in {clock.Elapsed.TotalSeconds:F1} s.");
    if (doctors > 0) await Console.Out.WriteLineAsync($"Bookable: {doctors} doctor(s), {visits} visit(s) added. MEDVYA_LOAD_DOCTORS={string.Join(',', calendars)}");
    return 0;
}

if (args is ["practice", ..])
{
    string Opt(string name, string fallback) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
    var practice = Opt("--practice", SyntheticPractice.DefaultPractice);
    var count = int.Parse(Opt("--count", "50000"), System.Globalization.CultureInfo.InvariantCulture);
    var mrnStart = int.Parse(Opt("--mrn-start", "100000"), System.Globalization.CultureInfo.InvariantCulture);
    var member = Opt("--member-user", "") is { Length: > 0 } m ? Guid.Parse(m) : (Guid?)null;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var r = await SyntheticPractice.SeedAsync(connectionString, environment, practice, count, mrnStart, member, Opt("--region", "centralindia"));
    await Console.Out.WriteLineAsync($"Practice '{practice}' tenant {r.Tenant} facility {r.Facility}: added {r.Patients} patient(s) (MRN {mrnStart} to {mrnStart + count - 1}), now {r.Doctors} doctor(s), {r.Nurses} nurse(s), {r.Receptionists} receptionist(s), {r.Admins} admin(s), {r.Rooms} room(s); {r.Visits} visit(s) added, in {clock.Elapsed.TotalSeconds:F1} s. Staff have no Entra accounts and cannot sign in.");
    return 0;
}

var includeUae = args.Contains("--uae");
await SyntheticSeed.SeedAsync(connectionString, environment, includeUae);
await Console.Out.WriteLineAsync($"Migrated and seeded {SyntheticSeed.Tenants(includeUae).Count} synthetic tenant(s).");
return 0;

// Internal on purpose: the ASP.NET Core framework (via Medvya.BuildingBlocks.Security) would otherwise make Program public and clash with Host.Api in the tests.
internal partial class Program;
