using System.Security.Cryptography;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Documents (MDV-P2-006). Who may do what is <see cref="DocumentRules"/>; clinicians also need a care relationship (MDV-P2-024). An upload
/// is checked by its own bytes, stored once under a random key, scanned, and only then recorded with the scan's result; nothing is viewable
/// until the result is Clean, and a pending one is looked at again whenever the list or a link is asked for. Every call writes its audit
/// event with the patient (an upload and an edit inside the transaction of their change).
/// </summary>
public sealed class DocumentService(
    EncountersDbContext db, IClinicalAccess clinical, IStaffAccess staff, IPatientDirectory patients, IDocumentStore store, IMalwareScan scan,
    ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IDocuments
{
    const int RefreshAtMost = 20;

    public async Task<DocumentList> ListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        var canOpen = await RequireListAsync(patientId, facilityId, cancellationToken);
        var pending = await db.Documents.Where(d => d.PatientId == patientId && d.ScanStatus == ScanStatus.Pending).OrderBy(d => d.UploadedAt).Take(RefreshAtMost).ToListAsync(cancellationToken);
        foreach (var d in pending) await RefreshAsync(d, cancellationToken);
        if (pending.Count > 0) await db.SaveChangesAsync(cancellationToken);
        var rows = await db.Documents.AsNoTracking().Where(d => d.PatientId == patientId).OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.UploadedAt).ToListAsync(cancellationToken);
        await audit.WriteAsync(Event(DocumentPolicy.ListAction, patientId, patientId), cancellationToken);
        return new([.. rows.Select(View)], canOpen);
    }

    public async Task<DocumentView> UploadAsync(Guid patientId, Guid facilityId, DocumentType type, DateOnly documentDate, ChartSource source, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await RequireListAsync(patientId, facilityId, cancellationToken);
        if (!Enum.IsDefined(type) || source is not (ChartSource.PatientReported or ChartSource.Imported) || !ValidDate(documentDate)) throw new DocumentRejectedException("invalid");
        var bytes = await ReadAsync(content, cancellationToken);
        var contentType = DocumentRules.ContentTypeOf(bytes) ?? throw new DocumentRejectedException("unsupported_type");
        var me = user.UserId ?? throw new InvalidOperationException("No user.");

        var key = Guid.NewGuid().ToString("N");
        await store.PutAsync(key, new MemoryStream(bytes), contentType, cancellationToken);
        var document = new ClinicalDocument
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = facilityId, PatientId = patientId, Type = type, DocumentDate = documentDate, Source = source,
            ContentType = contentType, SizeBytes = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), BlobKey = key,
            ScanStatus = ScanStatus.Pending, UploadedBy = me, UploadedAt = clock.GetUtcNow(),
        };
        await RefreshAsync(document, cancellationToken); // Clean or Infected at once with a synchronous scanner; Pending until Defender has written its result
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(DocumentPolicy.UploadAction, patientId, document.Id), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(document);
    }

    public async Task<(DocumentLink Link, Guid PatientId)?> ViewLinkAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken) is not { } document) return null;
        // The front desk never opens a clinical document, so only the clinical gate applies here.
        await clinical.RequireAsync(document.PatientId, document.FacilityId, Capability.Patient360Chart, DocumentRules.Treats, cancellationToken);
        if (document.ScanStatus == ScanStatus.Pending)
        {
            await RefreshAsync(document, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (document.ScanStatus != ScanStatus.Clean) throw new DocumentNotViewableException(document.ScanStatus);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var url = await store.ViewLinkAsync(document.BlobKey, document.ContentType, me, DocumentPolicy.LinkLifetime, cancellationToken);
        await audit.WriteAsync(Event(DocumentPolicy.ViewAction, document.PatientId, document.Id), cancellationToken);
        return (new DocumentLink(url, clock.GetUtcNow() + DocumentPolicy.LinkLifetime, document.ContentType), document.PatientId);
    }

    public async Task<DocumentView?> UpdateAsync(Guid documentId, DocumentType type, DateOnly documentDate, CancellationToken cancellationToken = default)
    {
        if (await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken) is not { } document) return null;
        await clinical.RequireAsync(document.PatientId, document.FacilityId, Capability.Patient360Chart, DocumentRules.Treats, cancellationToken);
        if (!Enum.IsDefined(type) || !ValidDate(documentDate)) throw new DocumentRejectedException("invalid");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        (document.Type, document.DocumentDate, document.ChangedBy, document.ChangedAt) = (type, documentDate, user.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken); // the file is not touched: only this row changes
        await audit.WriteAsync(Event(DocumentPolicy.UpdateAction, document.PatientId, document.Id), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(document);
    }

    /// <summary>Asks the scanner and, for an infected file, quarantines it before the status says so.</summary>
    async Task RefreshAsync(ClinicalDocument document, CancellationToken cancellationToken)
    {
        var verdict = await scan.CheckAsync(document.BlobKey, cancellationToken);
        if (verdict == ScanVerdict.Infected) await store.QuarantineAsync(document.BlobKey, cancellationToken);
        document.ScanStatus = verdict switch { ScanVerdict.Clean => ScanStatus.Clean, ScanVerdict.Infected => ScanStatus.Infected, _ => ScanStatus.Pending };
    }

    /// <summary>List and upload: a clinician with a care relationship, or the front desk for a patient of the session's facility. True when the caller may open files.</summary>
    async Task<bool> RequireListAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken)
    {
        var granted = await staff.AccessAsync([Capability.Patient360Chart, Capability.RegisterBookPatients, Capability.CheckInQueue], facilityId, cancellationToken);
        if (DocumentRules.CanOpen(granted[0]))
        {
            await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, DocumentRules.Treats, cancellationToken);
            return true;
        }
        if (!DocumentRules.CanUploadAtDesk(granted[1], granted[2])) throw new ClinicalAccessDeniedException(false, false);
        // The desk works with its own facility's patients; another facility's patient, or another tenant's, is refused as not found would be: no access.
        if ((await patients.SummariesAsync([patientId], cancellationToken)).GetValueOrDefault(patientId) is not { } p || p.FacilityId != facilityId)
            throw new ClinicalAccessDeniedException(false, false);
        return false;
    }

    bool ValidDate(DateOnly date) => date >= new DateOnly(1900, 1, 1) && date <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1); // a day of slack for time zones ahead of UTC

    static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int n;
        while ((n = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + n > DocumentPolicy.MaxBytes) throw new DocumentRejectedException("too_large");
            buffer.Write(chunk, 0, n);
        }
        return buffer.Length == 0 ? throw new DocumentRejectedException("empty") : buffer.ToArray();
    }

    AuditEvent Event(string action, Guid patientId, Guid resourceId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = "document", ResourceId = resourceId, PatientId = patientId,
        Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    static DocumentView View(ClinicalDocument d) => new(d.Id, d.Type, d.DocumentDate, d.Source, d.ScanStatus, d.ContentType, d.SizeBytes, d.UploadedBy, d.UploadedAt);
}

/// <summary>Who may use documents, by role matrix level (MDV-P2-006). Pure, so a test can run it over every role.</summary>
public static class DocumentRules
{
    /// <summary>Clinicians who treat patients: the same levels as Patient 360.</summary>
    public static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];

    public static bool CanOpen(Access patient360) => Treats.Contains(patient360);

    /// <summary>The front desk: register/book and check-in both at Full (reception; not the practice admin, who has no business in clinical documents).</summary>
    public static bool CanUploadAtDesk(Access registerBook, Access checkIn) => registerBook == Access.Full && checkIn == Access.Full;

    /// <summary>The type from the file's first bytes, or null when it is not a pdf, jpeg or png.</summary>
    public static string? ContentTypeOf(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith("%PDF-"u8) ? "application/pdf"
        : bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? "image/jpeg"
        : bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? "image/png" : null;
}
