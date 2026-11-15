using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Microsoft.Extensions.Caching.Memory;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Logos in a private Blob container (no public access). Reads go through a read-only SAS link that expires. Azure: the managed identity and a
/// user-delegation SAS (the container is made by Bicep; the app cannot create it). Local Azurite: the connection string, account-key links.
/// A key is random and written once (a new upload gets a new key), so the bytes read for a printed paper are kept in memory by key and a changed
/// logo is simply another key: every PDF after the first prints without a storage round trip (#1365 F-067).
/// </summary>
public sealed class BlobLogoStore : ILogoStore, IDisposable
{
    public const string Container = "practice-assets";
    /// <summary>The delegation key outlives the link it signs by this much, so the next links reuse it.</summary>
    static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromHours(1);
    readonly BlobServiceClient service;
    readonly BlobContainerClient container;
    readonly bool userDelegation;
    // shortcut: 32 MB of images per instance, an hour after the last use; raise it when more clinics than that print within the hour.
    readonly MemoryCache images = new(new MemoryCacheOptions { SizeLimit = 32 * 1024 * 1024 });
    UserDelegationKey? delegationKey;
    bool containerReady;

    public BlobLogoStore(string connectionString) : this(new BlobServiceClient(connectionString), userDelegation: false) { }

    public BlobLogoStore(Uri blobEndpoint) : this(new BlobServiceClient(blobEndpoint, AzureCredentials.Shared), userDelegation: true) { }

    internal BlobLogoStore(BlobServiceClient service, bool userDelegation)
    {
        (this.service, this.userDelegation) = (service, userDelegation);
        container = service.GetBlobContainerClient(Container);
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        if (!userDelegation && !containerReady)
        {
            await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            containerReady = true;
        }
        await container.GetBlobClient(key).UploadAsync(new BinaryData(content), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
        Keep(key, content);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        images.Remove(key);
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        images.TryGetValue(key, out byte[]? kept) ? kept! : Keep(key, (await container.GetBlobClient(key).DownloadContentAsync(cancellationToken)).Value.Content.ToArray());

    byte[] Keep(string key, byte[] content) =>
        images.Set(key, content, new MemoryCacheEntryOptions { Size = content.Length, SlidingExpiration = TimeSpan.FromHours(1) });

    public async Task<Uri> LinkForAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var blob = container.GetBlobClient(key);
        var now = DateTimeOffset.UtcNow;
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, now + lifetime) { BlobContainerName = Container, BlobName = key, Resource = "b", StartsOn = now.AddMinutes(-1) };
        if (!userDelegation) return blob.GenerateSasUri(sas);
        // One delegation key signs every link until it runs out: a round trip to the Blob service an hour, not one per link.
        if (delegationKey is not { } signer || signer.SignedExpiresOn < sas.ExpiresOn)
            delegationKey = signer = await service.GetUserDelegationKeyAsync(now.AddMinutes(-1), sas.ExpiresOn + DelegationKeyLifetime, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(signer, service.AccountName) }.ToUri();
    }

    public void Dispose() => images.Dispose();
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
