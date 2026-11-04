using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Medvya.Tenancy.Application;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Logos in a private Blob container (no public access). Reads go through a read-only SAS link that expires.
/// shortcut: the link is signed with the account key of the connection string; switch to a user-delegation SAS with the managed
/// identity once the Azure environments exist (MDV-P0-006).
/// </summary>
public sealed class BlobLogoStore(string connectionString) : ILogoStore
{
    const string Container = "practice-assets";
    readonly BlobContainerClient container = new(connectionString, Container);

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await container.GetBlobClient(key).UploadAsync(new BinaryData(content), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public Uri LinkFor(string key, TimeSpan lifetime) =>
        container.GetBlobClient(key).GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow + lifetime);
}

/// <summary>No storage configured: logo calls fail closed instead of storing anywhere else.</summary>
public sealed class NoLogoStore : ILogoStore
{
    public Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default) => throw Unavailable();
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw Unavailable();
    public Uri LinkFor(string key, TimeSpan lifetime) => throw Unavailable();
    static InvalidOperationException Unavailable() => new("Blob storage is not configured (Storage:ConnectionString).");
}
