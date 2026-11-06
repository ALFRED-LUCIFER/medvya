using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// One pooled data source per connection string, shared by every module, job and tool (MDV-P0-042).
/// Azure Database for PostgreSQL is Entra-only (password login is off, infra/bicep/postgres.bicep), so a connection string for an Azure host
/// with no password signs in with an Entra access token: the managed identity in Azure, the developer's az login locally. The token is
/// refreshed in the background; a connection already open keeps working after its token expires.
/// </summary>
public static class PostgresDataSources
{
    public const string EntraScope = "https://ossrdbms-aad.database.windows.net/.default";

    static readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> Sources = new();

    public static NpgsqlDataSource For(string connectionString) =>
        Sources.GetOrAdd(connectionString, cs => new(() => Create(cs, new DefaultAzureCredential()))).Value;

    /// <summary>True for an Azure Database for PostgreSQL host without a password in the connection string.</summary>
    public static bool UsesEntraToken(NpgsqlConnectionStringBuilder cs) =>
        string.IsNullOrEmpty(cs.Password) && cs.Host?.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase) == true;

    public static NpgsqlDataSource Create(string connectionString, TokenCredential credential)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        if (UsesEntraToken(builder.ConnectionStringBuilder))
            // Entra tokens live 60-90 minutes; refresh well before, and retry quickly after a failure.
            builder.UsePeriodicPasswordProvider(
                async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext([EntraScope]), ct)).Token,
                TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
        return builder.Build();
    }
}
