using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Consult notes (MDV-P2-009). Clinical access first, then the save: a stale base version is refused up front and (for a race between two saves)
/// at the database by the version concurrency token, so no save is lost silently. Audit rows carry the patient, never the text.
/// </summary>
public sealed class ConsultNoteService(
    EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock,
    IPatientDirectory patients, IStaffUsers staff, IFacilityDirectory facilities) : IConsultNotes
{
    public const string ReadAction = "note.read", SaveAction = "note.save", SignAction = "note.sign", AddendumAction = "note.addendum", PdfAction = "note.pdf",
        PendingAction = "note.pending", Resource = "note";
    static readonly Access[] Leads = [Access.Full], SignLevel = [Access.Sign];

    public async Task<NoteView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var note = await db.ConsultNotes.AsNoTracking().SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, encounter, note?.Id), cancellationToken);
        return note is null ? Empty(encounterId) : await ViewAsync(note, cancellationToken);
    }

    public async Task<NoteView?> SignAsync(Guid encounterId, SignNote command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        // The endpoint already needs Sign on Clinical notes (licence gate included); this adds the care relationship.
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.ClinicalNotes, SignLevel, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var now = RequireReauth();
        var note = await db.ConsultNotes.SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        if (note?.Status == NoteStatus.Signed) return await ViewAsync(note, cancellationToken); // a double click or retry: nothing new is written
        if (command.BaseVersion != (note?.Version ?? 0)) throw new StaleNoteException(note?.Version ?? 0);

        var template = note is null ? NoteTemplates.Latest(NoteTemplates.Default) : NoteTemplates.Find(note.TemplateKey, note.TemplateVersion);
        var blanks = template!.Sections.Where(s => s.Required && string.IsNullOrWhiteSpace(note?.Sections.GetValueOrDefault(s.Id))).Select(s => ($"sections.{s.Id}", "required")).ToList();
        if (blanks.Count > 0) throw new NoteValidationException(blanks);

        var signed = new SignedNoteVersion
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, EncounterId = encounterId, NoteId = note!.Id, VersionNo = 1, TemplateKey = note.TemplateKey,
            TemplateVersion = note.TemplateVersion, Manual = note.Manual, Sections = note.Sections, SignedBy = me, SignedAt = now,
            ContentHash = SignedNoteVersion.Hash(note.TemplateKey, note.TemplateVersion, note.Sections),
        };
        (note.Status, note.Version, note.UpdatedBy, note.UpdatedAt) = (NoteStatus.Signed, note.Version + 1, me, now);
        db.SignedNoteVersions.Add(signed);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(Event(SignAction, encounter, note.Id), tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        // A save or a second signature won the race: the note is no longer the version this one read.
        catch (Exception e) when (e is DbUpdateConcurrencyException || e is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } })
        {
            db.ChangeTracker.Clear();
            var current = await db.ConsultNotes.AsNoTracking().SingleAsync(n => n.EncounterId == encounterId, cancellationToken);
            if (current.Status == NoteStatus.Signed) return await ViewAsync(current, cancellationToken);
            throw new StaleNoteException(current.Version);
        }
        return View(note, [signed]);
    }

    public async Task<NoteView?> AddAddendumAsync(Guid encounterId, AddNoteAddendum command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        // The same gate as signing (MDV-P2-012): the endpoint needs Sign on Clinical notes; this adds the care relationship and the re-authentication.
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.ClinicalNotes, SignLevel, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var now = RequireReauth();
        var note = await db.ConsultNotes.AsNoTracking().SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        if (note?.Status != NoteStatus.Signed) throw new NoteNotSignedException();
        var errors = new List<(string, string)>();
        foreach (var (field, value, max) in new[] { ("text", command.Text, ConsultNote.MaxSectionLength), ("reason", command.Reason, SignedNoteVersion.MaxReasonLength) })
            if (string.IsNullOrWhiteSpace(value)) errors.Add((field, "required"));
            else if (value.Length > max) errors.Add((field, "too_long"));
        if (errors.Count > 0) throw new NoteValidationException(errors);

        var last = await db.SignedNoteVersions.AsNoTracking().Where(v => v.NoteId == note.Id).OrderByDescending(v => v.VersionNo).FirstAsync(cancellationToken);
        var (sections, reason) = (new Dictionary<string, string> { [SignedNoteVersion.AddendumSection] = command.Text! }, command.Reason!.Trim());
        db.SignedNoteVersions.Add(new SignedNoteVersion
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, EncounterId = encounterId, NoteId = note.Id, VersionNo = last.VersionNo + 1, TemplateKey = note.TemplateKey,
            TemplateVersion = note.TemplateVersion, Manual = note.Manual, Sections = sections, SignedBy = me, SignedAt = now, AmendsVersionId = last.Id, Reason = reason,
            ContentHash = SignedNoteVersion.Hash(note.TemplateKey, note.TemplateVersion, sections, last.Id, reason),
        });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(Event(AddendumAction, encounter, note.Id), tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        // Another addendum took this version number at the same moment: nothing of this one was written; the doctor sees it and decides.
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            throw new StaleNoteException(note.Version);
        }
        return await ViewAsync(note, cancellationToken);
    }

    public async Task<NotePdf?> PdfAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var note = await db.ConsultNotes.AsNoTracking().SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        if (note?.Status != NoteStatus.Signed) throw new NoteNotSignedException();
        var versions = await db.SignedNoteVersions.AsNoTracking().Where(v => v.NoteId == note.Id).OrderBy(v => v.VersionNo).ToListAsync(cancellationToken);
        var patient = (await patients.SummariesAsync([encounter.PatientId], cancellationToken)).GetValueOrDefault(encounter.PatientId)
            ?? throw new InvalidOperationException("The encounter's patient was not found.");
        var names = await staff.DisplayNamesAsync([.. versions.Select(v => v.SignedBy).Distinct()], cancellationToken);
        var facility = await facilities.FindAsync(encounter.FacilityId, cancellationToken) ?? throw new InvalidOperationException("The encounter's facility was not found.");
        var content = SignedNotePdfDocument.Render(patient, facility.Name, TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone),
            NoteTemplates.Find(note.TemplateKey, note.TemplateVersion)!, [.. versions.Select(v => new SignedNotePdfDocument.Version(v, names.GetValueOrDefault(v.SignedBy) ?? "?"))]);
        var pdf = new SignedNotePdf
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, NoteId = note.Id, VersionNo = versions[^1].VersionNo, Content = content,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)), RenderedBy = me, RenderedAt = clock.GetUtcNow(),
        };
        db.SignedNotePdfs.Add(pdf);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(PdfAction, encounter, note.Id), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new NotePdf(content, pdf.Sha256);
    }

    /// <summary>The time now, once the session's re-authentication is no older than the window (and not from the future): signing and addenda.</summary>
    // shortcut: the stamp is a claim in the session cookie, set by /bff/reauth and checked here; the idle session row is not asked. Upgrade to a column on the session if a stamp must be revocable.
    DateTimeOffset RequireReauth()
    {
        var now = clock.GetUtcNow();
        if (user.ReauthenticatedAt is not { } at || now - at > SignedNoteVersion.ReauthWindow || at > now.AddMinutes(1)) throw new ReauthRequiredException();
        return now;
    }

    public async Task<(IReadOnlyList<PendingNote> Notes, bool More)> PendingAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var open = await db.Encounters.AsNoTracking()
            .Where(e => e.LeadClinicianId == me && e.FacilityId == facilityId && !db.ConsultNotes.Any(n => n.EncounterId == e.Id && n.Status == NoteStatus.Signed))
            // shortcut: the oldest 50 only, flagged as "more"; page the list if a pilot doctor routinely carries more than 50 unsigned notes.
            .OrderBy(e => e.StartedAt).Take(IConsultNotes.MaxPending + 1)
            .Select(e => new { Encounter = e, Drafted = db.ConsultNotes.Any(n => n.EncounterId == e.Id) }).ToListAsync(cancellationToken);
        List<PendingNote> pending = [];
        foreach (var row in open.Take(IConsultNotes.MaxPending))
        {
            try
            {
                // The doctor led it, but a care relationship can end (reassignment, policy change): a note they may no longer open is not offered.
                await clinical.RequireAsync(row.Encounter.PatientId, facilityId, Capability.Patient360Chart, Leads, cancellationToken);
            }
            catch (ClinicalAccessDeniedException)
            {
                continue;
            }
            await audit.WriteAsync(Event(PendingAction, row.Encounter, null), cancellationToken);
            pending.Add(new(row.Encounter.Id, row.Encounter.PatientId, row.Encounter.StartedAt, row.Drafted));
        }
        return (pending, open.Count > IConsultNotes.MaxPending);
    }

    async Task<NoteView> ViewAsync(ConsultNote note, CancellationToken cancellationToken) =>
        View(note, note.Status != NoteStatus.Signed ? [] : await db.SignedNoteVersions.AsNoTracking().Where(v => v.NoteId == note.Id).OrderBy(v => v.VersionNo).ToListAsync(cancellationToken));

    public async Task<NoteView?> SaveAsync(Guid encounterId, SaveNote command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var note = await db.ConsultNotes.SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);

        if (note?.Status == NoteStatus.Signed) throw new NoteSignedException(); // the database refuses it too (trigger)
        if (command.BaseVersion != (note?.Version ?? 0)) throw new StaleNoteException(note?.Version ?? 0);

        var key = command.TemplateKey ?? note?.TemplateKey ?? NoteTemplates.Default;
        // A note keeps the template version it was written on; a different template starts on that template's latest.
        var template = note is not null && key == note.TemplateKey ? NoteTemplates.Find(key, note.TemplateVersion) : NoteTemplates.Latest(key);
        var input = command.Sections ?? new Dictionary<string, string>();
        var errors = new List<(string, string)>();
        if (template is null) errors.Add(("templateKey", "invalid"));
        foreach (var (id, text) in input)
        {
            if (template is not null && !template.Sections.Any(s => s.Id == id)) errors.Add(($"sections.{id}", "unknown_section"));
            else if (text.Length > ConsultNote.MaxSectionLength) errors.Add(($"sections.{id}", "too_long"));
        }
        if (errors.Count > 0) throw new NoteValidationException(errors);

        var sections = input.Where(s => !string.IsNullOrWhiteSpace(s.Value)).ToDictionary(s => s.Key, s => s.Value);
        if (note is null) db.ConsultNotes.Add(note = new() { Id = Guid.CreateVersion7(), TenantId = TenantId, EncounterId = encounterId, Status = NoteStatus.Draft });
        (note.TemplateKey, note.TemplateVersion, note.Manual, note.Sections, note.Version, note.UpdatedBy, note.UpdatedAt) =
            (template!.Key, template.Version, command.Manual, sections, command.BaseVersion + 1, me, clock.GetUtcNow());
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(Event(SaveAction, encounter, note.Id), tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        // Two saves on the same version at once: the loser's update matches no row, or its insert meets the first one's.
        catch (Exception e) when (e is DbUpdateConcurrencyException || e is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } })
        {
            db.ChangeTracker.Clear();
            throw new StaleNoteException(await db.ConsultNotes.AsNoTracking().Where(n => n.EncounterId == encounterId).Select(n => n.Version).SingleOrDefaultAsync(cancellationToken));
        }
        return View(note);
    }

    AuditEvent Event(string action, Encounter e, Guid? noteId) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = noteId ?? e.Id,
        PatientId = e.PatientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    static NoteView Empty(Guid encounterId) => new(encounterId, NoteTemplates.Default, NoteTemplates.Latest(NoteTemplates.Default)!.Version, NoteStatus.Draft, false, new Dictionary<string, string>(), 0, null, null, []);

    /// <summary>Version 1 is the signature; every later version is an addendum, numbered with the version it follows.</summary>
    static NoteView View(ConsultNote n, IReadOnlyList<SignedNoteVersion>? versions = null)
    {
        var all = versions ?? [];
        var numbers = all.ToDictionary(v => v.Id, v => v.VersionNo);
        var signature = all.FirstOrDefault(v => v.VersionNo == 1) is { } first ? new NoteSignature(first.VersionNo, first.SignedBy, first.SignedAt, first.ContentHash) : null;
        var addenda = all.Where(v => v.AmendsVersionId is not null)
            .Select(v => new NoteAddendum(v.VersionNo, numbers[v.AmendsVersionId!.Value], v.Reason!, v.Sections[SignedNoteVersion.AddendumSection], v.SignedBy, v.SignedAt, v.ContentHash)).ToList();
        return new(n.EncounterId, n.TemplateKey, n.TemplateVersion, n.Status, n.Manual, n.Sections, n.Version, n.UpdatedAt, signature, addenda);
    }
}
