using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Medvya.Encounters.Application;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Documents in a private Blob container (no public access) under random keys. The original is written once with "only if it does not exist"
/// and never replaced. A view link is a read-only SAS that expires in minutes: in Azure (managed identity) a user-delegation SAS, so it is
/// signed by Entra, not by the account key, and carries the viewer's id as its correlation id for the storage logs; with a connection string
/// (local Azurite, Development only) an account-key SAS.
/// shortcut: a SAS is a bearer link, so within its few minutes anyone who holds it can fetch the file; the viewer's id is in the audit event and
/// the storage logs but the link is not bound to a device. Move to streaming through the API if that is not enough. The user-delegation path
/// needs an Entra token and cannot run against Azurite: the first Azure dev run must confirm it.
/// </summary>
public sealed class BlobDocumentStore(BlobServiceClient service, bool userDelegation, bool createContainers) : IDocumentStore
{
    public const string Live = "clinical-documents", Quarantine = "documents-quarantine";
    readonly BlobContainerClient live = service.GetBlobContainerClient(Live), quarantine = service.GetBlobContainerClient(Quarantine);
    bool ready;

    async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (ready || !createContainers) return;
        await live.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await quarantine.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        ready = true;
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        try
        {
            await live.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            }, cancellationToken);
        }
        catch (RequestFailedException e) when (e.Status == 409 || e.Status == 412)
        {
            throw new InvalidOperationException("A document is never overwritten.", e);
        }
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        (await live.GetBlobClient(key).DownloadContentAsync(cancellationToken)).Value.Content.ToArray();

    public async Task<Uri> ViewLinkAsync(string key, string contentType, Guid viewer, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var blob = live.GetBlobClient(key);
        var now = DateTimeOffset.UtcNow;
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, now + lifetime)
        {
            BlobContainerName = Live, BlobName = key, Resource = "b", StartsOn = now.AddMinutes(-1),
            ContentType = contentType, ContentDisposition = "inline", CacheControl = "no-store",
        };
        if (!userDelegation) return blob.GenerateSasUri(sas);
        sas.CorrelationId = viewer.ToString();
        var delegationKey = await service.GetUserDelegationKeyAsync(now.AddMinutes(-1), now + lifetime, cancellationToken);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(delegationKey.Value, service.AccountName) }.ToUri();
    }

    public async Task QuarantineAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        var source = live.GetBlobClient(key);
        await using (var read = await source.OpenReadAsync(cancellationToken: cancellationToken))
            await quarantine.GetBlobClient(key).UploadAsync(read, overwrite: true, cancellationToken);
        await source.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    /// <summary>The Defender result or the local check reads the stored bytes or tags through this.</summary>
    internal BlobClient Blob(string key) => live.GetBlobClient(key);
}

/// <summary>No storage configured: every document call fails closed.</summary>
public sealed class NoDocumentStore : IDocumentStore, IMalwareScan
{
    public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) => throw new DocumentStorageUnavailableException();
    public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default) => throw new DocumentStorageUnavailableException();
    public Task<Uri> ViewLinkAsync(string key, string contentType, Guid viewer, TimeSpan lifetime, CancellationToken cancellationToken = default) => throw new DocumentStorageUnavailableException();
    public Task QuarantineAsync(string key, CancellationToken cancellationToken = default) => throw new DocumentStorageUnavailableException();
    public Task<ScanVerdict> CheckAsync(string key, CancellationToken cancellationToken = default) => throw new DocumentStorageUnavailableException();
}

/// <summary>
/// Defender for Storage on-upload malware scanning (MDV-P2-006): it writes the verdict as the blob index tag "Malware Scanning scan result".
/// No tag yet is Pending (not viewable); "No threats found" is Clean; any other value is treated as infected, so an unknown result fails closed.
/// shortcut: the plan must be switched on for the storage account (an owner decision, cost; not in the dev Bicep). Until it is, every file stays
/// Pending and nothing can be opened. It has not been run against Defender: the tag name and values come from Microsoft's documentation.
/// </summary>
public sealed class DefenderMalwareScan(BlobDocumentStore store) : IMalwareScan
{
    const string Tag = "Malware Scanning scan result", Clean = "No threats found";

    public async Task<ScanVerdict> CheckAsync(string key, CancellationToken cancellationToken = default)
    {
        var tags = (await store.Blob(key).GetTagsAsync(cancellationToken: cancellationToken)).Value.Tags;
        return !tags.TryGetValue(Tag, out var result) ? ScanVerdict.Pending : result == Clean ? ScanVerdict.Clean : ScanVerdict.Infected;
    }
}

/// <summary>
/// Development stand-in used with local Azurite only (a connection string): it finds the standard EICAR test string and calls everything else
/// clean. It is not a malware scanner and is never registered for a managed-identity (Azure) storage account.
/// </summary>
public sealed class LocalEicarScan(BlobDocumentStore store) : IMalwareScan
{
    // Split in two so no antivirus flags this source file.
    static readonly byte[] Eicar = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$" + "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

    public async Task<ScanVerdict> CheckAsync(string key, CancellationToken cancellationToken = default)
    {
        var bytes = (await store.Blob(key).DownloadContentAsync(cancellationToken)).Value.Content.ToMemory();
        return bytes.Span.IndexOf(Eicar) >= 0 ? ScanVerdict.Infected : ScanVerdict.Clean;
    }
}
