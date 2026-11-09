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
    public const string ReadAction = "note.read", SaveAction = "note.save", Resource = "note";
    static readonly Access[] Leads = [Access.Full];

    public async Task<NoteView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var note = await db.ConsultNotes.AsNoTracking().SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, encounter, note?.Id), cancellationToken);
        return note is null ? Empty(encounterId) : View(note);
    }

    public async Task<NoteView?> SaveAsync(Guid encounterId, SaveNote command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        var note = await db.ConsultNotes.SingleOrDefaultAsync(n => n.EncounterId == encounterId, cancellationToken);

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

    static NoteView View(ConsultNote n) => new(n.EncounterId, n.TemplateKey, n.TemplateVersion, n.Status, n.Manual, n.Sections, n.Version, n.UpdatedAt);
}
