using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Consult notes (MDV-P2-009). Clinical access first, then the save: a stale base version is refused up front and (for a race between two saves)
/// at the database by the version concurrency token, so no save is lost silently. Audit rows carry the patient, never the text.
/// </summary>
public sealed class ConsultNoteService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IConsultNotes
{
    public const string ReadAction = "note.read", SaveAction = "note.save", SignAction = "note.sign", PendingAction = "note.pending", Resource = "note";
    static readonly Access[] Leads = [Access.Full], SignLevel = [Access.Sign];

    public async Task<NoteView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var note = await db.ConsultNotes.AsNoTracking().SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, encounter, note?.Id), cancellationToken);
        return note is null ? Empty(encounterId) : View(note, await SignatureOf(note, cancellationToken));
    }

    public async Task<NoteView?> SignAsync(Guid encounterId, SignNote command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        // The endpoint already needs Sign on Clinical notes (licence gate included); this adds the care relationship.
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.ClinicalNotes, SignLevel, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var now = clock.GetUtcNow();
        // shortcut: the stamp is a claim in the session cookie, set by /bff/reauth and checked here; the idle session row is not asked. Upgrade to a column on the session if a stamp must be revocable.
        if (user.ReauthenticatedAt is not { } at || now - at > SignedNoteVersion.ReauthWindow || at > now.AddMinutes(1)) throw new ReauthRequiredException();
        var note = await db.ConsultNotes.SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        if (note?.Status == NoteStatus.Signed) return View(note, await SignatureOf(note, cancellationToken)); // a double click or retry: nothing new is written
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
            if (current.Status == NoteStatus.Signed) return View(current, await SignatureOf(current, cancellationToken));
            throw new StaleNoteException(current.Version);
        }
        return View(note, new NoteSignature(signed.VersionNo, signed.SignedBy, signed.SignedAt, signed.ContentHash));
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

    async Task<NoteSignature?> SignatureOf(ConsultNote note, CancellationToken cancellationToken) =>
        note.Status != NoteStatus.Signed ? null
        : await db.SignedNoteVersions.AsNoTracking().Where(v => v.NoteId == note.Id && v.VersionNo == 1)
            .Select(v => new NoteSignature(v.VersionNo, v.SignedBy, v.SignedAt, v.ContentHash)).SingleOrDefaultAsync(cancellationToken);

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

    static NoteView Empty(Guid encounterId) => new(encounterId, NoteTemplates.Default, NoteTemplates.Latest(NoteTemplates.Default)!.Version, NoteStatus.Draft, false, new Dictionary<string, string>(), 0, null);

    static NoteView View(ConsultNote n, NoteSignature? signature = null) => new(n.EncounterId, n.TemplateKey, n.TemplateVersion, n.Status, n.Manual, n.Sections, n.Version, n.UpdatedAt, signature);
}
