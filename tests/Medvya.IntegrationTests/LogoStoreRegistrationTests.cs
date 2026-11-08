using Medvya.Tenancy.Application;
using Medvya.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>#211: on Azure only Storage:BlobEndpoint is set (managed identity); the logo store must not fall back to the fail-closed one.</summary>
public class LogoStoreRegistrationTests
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
}
