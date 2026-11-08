using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Medvya.IntegrationTests;

/// <summary>
/// A real Azurite blob service in Docker for the document tests (MDV-P2-006): real uploads, real SAS links, real fetches. The account key is
/// random per run (Azurite takes it from AZURITE_ACCOUNTS), so no key, not even a well-known one, is written in the repository.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    const string Account = "medvyatest";
    readonly string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    IContainer? container;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        container = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(10000, true)
            .WithEnvironment("AZURITE_ACCOUNTS", $"{Account}:{key}")
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--loose", "--skipApiVersionCheck")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(10000))
            .Build();
        await container.StartAsync();
        ConnectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={key};BlobEndpoint=http://127.0.0.1:{container.GetMappedPublicPort(10000)}/{Account};";
    }

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}
