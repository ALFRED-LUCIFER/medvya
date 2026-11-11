using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Medvya.Tenancy.Application;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Logos in a private Blob container (no public access). Reads go through a read-only SAS link that expires. Azure: the managed identity and a
/// user-delegation SAS (the container is made by Bicep; the app cannot create it). Local Azurite: the connection string, account-key links.
/// </summary>
public sealed class BlobLogoStore : ILogoStore
{
    public const string Container = "practice-assets";
    readonly BlobServiceClient service;
    readonly BlobContainerClient container;
    readonly bool userDelegation;

    public BlobLogoStore(string connectionString) : this(new BlobServiceClient(connectionString), userDelegation: false) { }

    public BlobLogoStore(Uri blobEndpoint) : this(new BlobServiceClient(blobEndpoint, new DefaultAzureCredential()), userDelegation: true) { }

    BlobLogoStore(BlobServiceClient service, bool userDelegation)
    {
        (this.service, this.userDelegation) = (service, userDelegation);
        container = service.GetBlobContainerClient(Container);
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        if (!userDelegation) await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await container.GetBlobClient(key).UploadAsync(new BinaryData(content), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        (await container.GetBlobClient(key).DownloadContentAsync(cancellationToken)).Value.Content.ToArray();

    public async Task<Uri> LinkForAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var blob = container.GetBlobClient(key);
        var now = DateTimeOffset.UtcNow;
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, now + lifetime) { BlobContainerName = Container, BlobName = key, Resource = "b", StartsOn = now.AddMinutes(-1) };
        if (!userDelegation) return blob.GenerateSasUri(sas);
        // shortcut: a delegation key is fetched per link; cache it for its lifetime if logo reads become frequent.
        var delegationKey = await service.GetUserDelegationKeyAsync(now.AddMinutes(-1), now + lifetime, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(delegationKey.Value, service.AccountName) }.ToUri();
    }
}

/// <summary>No storage configured: logo calls fail closed instead of storing anywhere else.</summary>
public sealed class NoLogoStore : ILogoStore
{
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task<Uri> LinkForAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default) => throw Unavailable();
    static InvalidOperationException Unavailable() => new("Blob storage is not configured (Storage:BlobEndpoint or Storage:ConnectionString).");
}
