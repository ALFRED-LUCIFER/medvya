using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// Encounters (MDV-P2-001). Clinical access first (role, care relationship, facility of the visit). Starting creates the encounter and
/// calls the patient in on the board in one transaction, then tells the open boards; a second start (or a simultaneous one, which the
/// unique index refuses) returns the same encounter. Every answer writes its audit event, with the patient, before it is returned.
/// </summary>
public sealed class EncounterService(
    EncountersDbContext db, IConsultVisits visits, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IEncounters
{
    public const string StartAction = "encounter.start", ReadAction = "encounter.read", EndAction = "encounter.end",
        ConfirmIdentityAction = "encounter.identity.confirm", Resource = "encounter";
    /// <summary>Starting and ending a consultation is a doctor's (Patient 360 at Full); reading the banner is any clinician's who treats.</summary>
    static readonly Access[] Leads = [Access.Full], Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];

    public async Task<EncounterView?> StartAsync(StartEncounter command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await visits.FindAsync(command.AppointmentId, cancellationToken) is not { } visit) return null;
        await clinical.RequireAsync(visit.PatientId, visit.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");

        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.AppointmentId == visit.Id, cancellationToken) is { } existing)
            return await RepeatAsync(existing, visit.Room, cancellationToken);
        // Switching patient needs a decision: end the open consultation, or say to leave it open (AC2). This visit's own encounter, from a
        // start that won the race since the read above, is not another consultation: the insert below meets it and answers with it.
        // shortcut: a check, then a write; the same doctor starting two patients in the same instant (two tabs) gets two open consultations
        // without choosing. No database rule fits (leaving one open is allowed); revisit if Today (MDV-P2-002) starts consultations in bulk.
        if (!command.LeaveOpen && await db.Encounters.AsNoTracking()
                .Where(e => e.LeadClinicianId == me && e.Status == EncounterStatus.Active && e.AppointmentId != visit.Id).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken) is { } open)
            throw new OpenEncounterException(open);

        var encounter = new Encounter
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, AppointmentId = visit.Id,
            LeadClinicianId = me, Status = EncounterStatus.Active, AuthorityMode = AuthorityMode.Medvya, StartedAt = clock.GetUtcNow(),
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Encounters.Add(encounter);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Started at the same moment from another tab or device: that encounter stands, and this start answers with it.
            await tx.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var winner = await db.Encounters.AsNoTracking().SingleAsync(x => x.AppointmentId == visit.Id, cancellationToken);
            return await RepeatAsync(winner, (await visits.FindAsync(visit.Id, cancellationToken))?.Room, cancellationToken);
        }
        var moved = await visits.StartAsync(visit.Id, tx.GetDbTransaction(), cancellationToken);
        await audit.WriteAsync(Event(StartAction, encounter), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await visits.NotifyAsync(moved, cancellationToken);
        return View(encounter, moved.Room);
    }

    public async Task<EncounterView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Treats, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, encounter), cancellationToken);
        return View(encounter, (await visits.FindAsync(encounter.AppointmentId, cancellationToken))?.Room);
    }

    public async Task<AllergyBanner?> AllergiesAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Treats, cancellationToken);
        return ChartReads.BannerOf(await db.Allergies.AsNoTracking().Where(a => a.PatientId == encounter.PatientId).ToListAsync(cancellationToken));
    }

    public async Task<EncounterView?> ConfirmIdentityAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var me = user.UserId ?? throw new InvalidOperationException("No user.");
        // Only the doctor in the room with the patient can say they confirmed it with them, not a colleague who may open the chart.
        if (encounter.LeadClinicianId != me) throw new ClinicalAccessDeniedException(canRequest: false, requestPending: false);
        // Allowed after End too: a note is often signed from Today after the consultation ended, and without this item it could never be signed.
        var now = clock.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // The first confirmation stands (a second tap, or two tabs at once, keeps who and when it was).
        if (await db.Encounters.Where(e => e.Id == encounterId && e.IdentityConfirmedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IdentityConfirmedAt, now).SetProperty(e => e.IdentityConfirmedBy, me), cancellationToken) == 1)
            (encounter.IdentityConfirmedAt, encounter.IdentityConfirmedBy) = (now, me);
        else encounter = await db.Encounters.AsNoTracking().SingleAsync(e => e.Id == encounterId, cancellationToken);
        await audit.WriteAsync(Event(ConfirmIdentityAction, encounter), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(encounter, (await visits.FindAsync(encounter.AppointmentId, cancellationToken))?.Room);
    }

    public async Task<EncounterView?> EndAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Leads, cancellationToken);
        var (now, me) = (clock.GetUtcNow(), user.UserId);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var ended = await db.Encounters.Where(e => e.Id == encounterId && e.Status == EncounterStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EncounterStatus.Ended).SetProperty(e => e.EndedAt, now).SetProperty(e => e.EndedBy, me), cancellationToken);
        ConsultVisit? moved = null;
        if (ended == 1)
        {
            (encounter.Status, encounter.EndedAt, encounter.EndedBy) = (EncounterStatus.Ended, now, me);
            moved = await visits.FinishAsync(encounter.AppointmentId, tx.GetDbTransaction(), cancellationToken);
        }
        else encounter = await db.Encounters.AsNoTracking().SingleAsync(e => e.Id == encounterId, cancellationToken); // already ended: answer as it is
        await audit.WriteAsync(Event(EndAction, encounter), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        if (moved is not null) await visits.NotifyAsync(moved, cancellationToken);
        return View(encounter, moved?.Room ?? (await visits.FindAsync(encounter.AppointmentId, cancellationToken))?.Room);
    }

    async Task<EncounterView> RepeatAsync(Encounter encounter, string? room, CancellationToken cancellationToken)
    {
        if (encounter.Status == EncounterStatus.Ended) throw new EncounterEndedException();
        await audit.WriteAsync(Event(StartAction, encounter), cancellationToken); // a repeat is still an audited start
        return View(encounter, room);
    }

    AuditEvent Event(string action, Encounter e) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = e.Id,
        PatientId = e.PatientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    static EncounterView View(Encounter e, string? room) =>
        new(e.Id, e.AppointmentId, e.PatientId, e.FacilityId, e.LeadClinicianId, e.Status, e.AuthorityMode, e.StartedAt, e.EndedAt, room, e.IdentityConfirmedAt, e.IdentityConfirmedBy);
}

/// <summary>The visits with a consultation open (MDV-P1-100): the board shows them as with the doctor and the desk cannot close or undo them.</summary>
public sealed class OpenConsultations(EncountersDbContext db) : IOpenConsultations
{
    public async Task<IReadOnlySet<Guid>> OpenAsync(IReadOnlyCollection<Guid> appointmentIds, CancellationToken cancellationToken = default) =>
        (await db.Encounters.AsNoTracking().Where(e => appointmentIds.Contains(e.AppointmentId) && e.Status == EncounterStatus.Active)
            .Select(e => e.AppointmentId).ToListAsync(cancellationToken)).ToHashSet();
}
