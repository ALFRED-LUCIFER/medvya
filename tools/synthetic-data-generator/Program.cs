using Medvya.SyntheticData;

// Usage: dotnet run --project tools/synthetic-data-generator [-- --uae]
// Needs DOTNET_ENVIRONMENT (Development, Staging or Test) and ConnectionStrings__Medvya; `pnpm dev:db` sets both locally.
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Medvya");
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync("ConnectionStrings__Medvya is not set.");
    return 1;
}

var includeUae = args.Contains("--uae");
await SyntheticSeed.SeedAsync(connectionString, environment, includeUae);
await Console.Out.WriteLineAsync($"Migrated and seeded {SyntheticSeed.Tenants(includeUae).Count} synthetic tenant(s).");
return 0;
