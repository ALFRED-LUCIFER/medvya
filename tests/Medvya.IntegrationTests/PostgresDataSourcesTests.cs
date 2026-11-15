using Azure.Core;
using Medvya.BuildingBlocks.Infrastructure;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-042: an Azure Database for PostgreSQL connection with no password signs in with an Entra token; everything else keeps its password.</summary>
public class PostgresDataSourcesTests
{
    [Theory]
    [InlineData("Host=psql-medvya-dev-abc.postgres.database.azure.com;Database=medvya;Username=app-medvya-dev", true)]
    [InlineData("Host=PSQL-X.POSTGRES.DATABASE.AZURE.COM;Database=medvya;Username=app", true)]
    [InlineData("Host=psql-x.postgres.database.azure.com;Database=medvya;Username=app;Password=p", false)]
    [InlineData("Host=localhost;Database=medvya;Username=medvya_app", false)]
    [InlineData("Host=postgres.database.azure.com.attacker.example;Database=medvya;Username=app", false)]
    public void OnlyAzureHostsWithoutAPasswordUseAToken(string connectionString, bool expected) =>
        Assert.Equal(expected, PostgresDataSources.UsesEntraToken(new NpgsqlConnectionStringBuilder(connectionString)));

    [Fact]
    public async Task AnAzureDataSourceAsksTheCredentialForADatabaseToken()
    {
        var credential = new RecordingCredential();
        await using var source = PostgresDataSources.Create("Host=psql-x.postgres.database.azure.com;Database=medvya;Username=app", credential);
        await credential.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([PostgresDataSources.EntraScope], credential.Scopes);
    }

    // ADR-014: the console holds several narrow logins, each signed in by its own identity; one credential for all would need one login holding every role.
    [Fact]
    public void ANamedIdentitySignsInItsOwnLoginAndNpgsqlNeverSeesTheKeyword()
    {
        const string login = "Host=psql-x.postgres.database.azure.com;Database=medvya;Username=id-review;SslMode=VerifyFull";
        var (named, namedCredential) = PostgresDataSources.Credential($"{login};{PostgresDataSources.ManagedIdentityKeyword}=00000000-0000-0000-0000-000000000001");
        Assert.IsType<Azure.Identity.ManagedIdentityCredential>(namedCredential);
        var parsed = new NpgsqlConnectionStringBuilder(named); // an unknown keyword would throw here
        Assert.Equal(("id-review", "medvya"), (parsed.Username, parsed.Database));

        var (plain, plainCredential) = PostgresDataSources.Credential(login);
        Assert.Equal(login, plain);
        Assert.IsType<Azure.Identity.DefaultAzureCredential>(plainCredential);
    }

    sealed class RecordingCredential : TokenCredential
    {
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string[] Scopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes;
            Asked.TrySetResult();
            return ValueTask.FromResult(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
