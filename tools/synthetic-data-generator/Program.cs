using Medvya.Patients.Infrastructure;
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

// Local column master key for Always Encrypted, kept outside the repository like user secrets; protects synthetic data only.
SyntheticSeed.EnsureNonProduction(environment);
var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Medvya", "local-column-master-key.pem");
var keys = LocalKeyStoreProvider.Register(() => LocalKeyStoreProvider.LoadOrCreate(keyFile));

var includeUae = args.Contains("--uae");
await SyntheticSeed.SeedAsync(connectionString, environment, includeUae, keys);
await Console.Out.WriteLineAsync($"Migrated and seeded {SyntheticSeed.Tenants(includeUae).Count} synthetic tenant(s).");
return 0;
