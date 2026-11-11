using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>The limits and audit action names of documents, shared by the use case and its endpoints.</summary>
public static class DocumentPolicy
{
    public const int MaxBytes = 10 * 1024 * 1024;
    /// <summary>A view link lives this long at most.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);
    public const string ListAction = "patient.document.list", UploadAction = "patient.document.upload", ViewAction = "patient.document.view", UpdateAction = "patient.document.update";
}

/// <summary>A document as the list shows it. Never the file or its blob key.</summary>
public sealed record DocumentView(Guid Id, DocumentType Type, DateOnly DocumentDate, ChartSource Source, ScanStatus ScanStatus, string ContentType, long SizeBytes, Guid UploadedBy, DateTimeOffset UploadedAt);

/// <summary><see cref="CanOpen"/> is false for the front desk: it sees the list and uploads, but opens nothing.</summary>
public sealed record DocumentList(IReadOnlyList<DocumentView> Documents, bool CanOpen);

/// <summary>A link that stops working after <see cref="ExpiresAt"/> (five minutes at most).</summary>
public sealed record DocumentLink(Uri Url, DateTimeOffset ExpiresAt, string ContentType);

/// <summary>An upload that cannot be accepted: empty, too large, not a pdf, jpeg or png, or details that are not valid.</summary>
public sealed class DocumentRejectedException(string code) : Exception($"Document rejected: {code}")
{
    public string Code { get; } = code;
}

/// <summary>The file has not passed its malware scan (pending) or failed it (quarantined): it cannot be opened.</summary>
public sealed class DocumentNotViewableException(ScanStatus status) : Exception("The document is not viewable.")
{
    public ScanStatus Status { get; } = status;
}

/// <summary>No object storage is configured: documents fail closed rather than going anywhere else.</summary>
public sealed class DocumentStorageUnavailableException() : Exception("Document storage is not configured (Storage:BlobEndpoint or Storage:ConnectionString).");

/// <summary>
/// Documents (MDV-P2-006). Listing and uploading are open to clinicians (care relationship, MDV-P2-024) and to the front desk
/// (upload only: it never opens a clinical document); opening, and editing the type or date, are clinicians' only. Every call writes its
/// audit event with the patient. A file is viewable only after its scan passed.
/// </summary>
public interface IDocuments
{
    Task<DocumentList> ListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="DocumentRejectedException"/>
    Task<DocumentView> UploadAsync(Guid patientId, Guid facilityId, DocumentType type, DateOnly documentDate, ChartSource source, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Null: no such document in this tenant.</summary>
    /// <exception cref="DocumentNotViewableException"/>
    Task<(DocumentLink Link, Guid PatientId)?> ViewLinkAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Changes the type and date only; the file is never touched. Null: no such document.</summary>
    /// <exception cref="DocumentRejectedException"/>
    Task<DocumentView?> UpdateAsync(Guid documentId, DocumentType type, DateOnly documentDate, CancellationToken cancellationToken = default);
}

public enum ScanVerdict { Pending, Clean, Infected }

/// <summary>Private object storage for documents. The original is written once and never replaced (no overwrite, no delete).</summary>
public interface IDocumentStore
{
    /// <exception cref="InvalidOperationException">The key already exists.</exception>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>The stored bytes, for a server-made document the API streams itself (the printed prescription behind a share link).</summary>
    Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>A read-only link that expires after <paramref name="lifetime"/>; <paramref name="viewer"/> is recorded in the storage logs where the platform allows it.</summary>
    Task<Uri> ViewLinkAsync(string key, string contentType, Guid viewer, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>Moves an infected file out of the live container; it is kept for the security team, not deleted.</summary>
    Task QuarantineAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>The malware scan seam: what the scanner says about one stored file. Defender for Storage in Azure; a local stand-in for development.</summary>
public interface IMalwareScan
{
    Task<ScanVerdict> CheckAsync(string key, CancellationToken cancellationToken = default);
}
