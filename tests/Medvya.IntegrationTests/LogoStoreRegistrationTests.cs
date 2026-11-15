using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>#211: on Azure only Storage:BlobEndpoint is set (managed identity); the logo store must not fall back to the fail-closed one.</summary>
public class LogoStoreRegistrationTests(AzuriteFixture blobs) : IClassFixture<AzuriteFixture>
{
    static ILogoStore StoreFor(string? connectionString, string? endpoint) =>
        new ServiceCollection().AddTenancyModule("Host=unused", connectionString, endpoint).BuildServiceProvider().GetRequiredService<ILogoStore>();

    [Fact]
    public void ABlobEndpointGivesTheManagedIdentityStore() =>
        Assert.IsType<BlobLogoStore>(StoreFor(null, "https://example.blob.core.windows.net"));

    [Fact]
    public async Task NoStorageSettingFailsClosed()
    {
        var store = StoreFor(null, null);
        Assert.IsType<NoLogoStore>(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync("k", [1], "image/png"));
    }

    /// <summary>#1365 F-066: a signed link costs no storage round trip while the delegation key still covers it; a link that outlives the key gets a new one.</summary>
    [Fact]
    public async Task LinksAreSignedWithOneDelegationKeyUntilItRunsOut()
    {
        var service = new CountingService();
        using var store = new BlobLogoStore(service, userDelegation: true);
        var first = await store.LinkForAsync("t/logo/a", TimeSpan.FromMinutes(5));
        var second = await store.LinkForAsync("t/logo/b", TimeSpan.FromMinutes(5));
        Assert.Equal(1, service.Keys);
        Assert.All(new[] { first, second }, link => Assert.Contains("skoid=", link.Query, StringComparison.Ordinal)); // signed by Entra, not an account key
        Assert.EndsWith("/practice-assets/t/logo/b", second.AbsolutePath, StringComparison.Ordinal);
        await store.LinkForAsync("t/logo/a", TimeSpan.FromHours(2));
        Assert.Equal(2, service.Keys);
    }

    /// <summary>#1365: an upload is not kept in memory (the owner's ID and registration certificates go through this store); only what a printed paper reads is.</summary>
    [Fact]
    public async Task AnUploadedDocumentIsNotKeptInMemory()
    {
        using var store = new BlobLogoStore(blobs.ConnectionString);
        var key = $"{Guid.NewGuid()}/onboarding/{Guid.NewGuid()}";
        await store.PutAsync(key, [0x25, 0x50, 0x44, 0x46], "application/pdf");
        await new BlobContainerClient(blobs.ConnectionString, BlobLogoStore.Container).DeleteBlobAsync(key); // behind the store's back: a kept copy would still answer

        Assert.Equal(404, (await Assert.ThrowsAsync<RequestFailedException>(() => store.ReadAsync(key))).Status);
    }

    sealed class CountingService() : BlobServiceClient(new Uri("https://example.blob.core.windows.net"), AzureCredentials.Shared)
    {
        public int Keys { get; private set; }

        public override Task<Response<UserDelegationKey>> GetUserDelegationKeyAsync(DateTimeOffset? startsOn, DateTimeOffset expiresOn, CancellationToken cancellationToken = default)
        {
            Keys++;
            var key = BlobsModelFactory.UserDelegationKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), startsOn ?? DateTimeOffset.UtcNow, expiresOn, "b", "2025-01-05", Convert.ToBase64String(new byte[32]));
            return Task.FromResult(Response.FromValue(key, null!));
        }
    }
}
