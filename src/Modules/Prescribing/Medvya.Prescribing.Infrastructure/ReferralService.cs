using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// Referrals (MDV-P2-019). Clinical access first (writes: Prescriptions and orders at Sign; the Patient 360 list: the chart's access), then the work
/// in one transaction with its audit event (purpose treatment, with the patient). A send needs the patient's consent when the channel discloses
/// (<see cref="ReferralRules.NeedsConsent"/>), asked of the consent record at that moment. A status move is a conditional update on the state the
/// caller saw, so two simultaneous moves cannot both win. The letter carries only the sections the doctor selected.
/// </summary>
public sealed class ReferralService(
    PrescribingDbContext db, IEncounterDirectory encounters, IDocumentDirectory documents, IClinicalAccess clinical, IPatientDirectory patients, IPatientConsents consents,
    IPatientChart chart, IFacilityDirectory facilities, IStaffUsers staffUsers, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IReferrals
{
    public const string CreateAction = "referral.create", FileInAction = "referral.file_in", SendAction = "referral.send", AdvanceAction = "referral.advance",
        ReadAction = "referral.read", LetterAction = "referral.letter", Resource = "referral";
    public const int MaxProvider = 200, MaxReason = 500, MaxNote = 200;
    static readonly Access[] Doctors = [Access.Sign], Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];

    public async Task<ReferralView?> CreateAsync(Guid encounterId, CreateReferral command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await encounters.FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        var errors = new List<(string, string)>();
        var provider = Text(command.Provider, "provider", MaxProvider, true, errors);
        var reason = Text(command.Reason, "reason", MaxReason, true, errors);
        if (command.Urgency is not { } urgency || !Enum.IsDefined(urgency)) errors.Add(("urgency", "required"));
        var sections = (command.Sections ?? []).Distinct().ToList();
        if (sections.Any(s => !Enum.IsDefined(s))) errors.Add(("sections", "invalid"));
        if (errors.Count > 0) throw new ReferralValidationException(errors);

        var referral = new Referral
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = encounter.FacilityId, PatientId = encounter.PatientId, EncounterId = encounter.Id,
            Direction = ReferralDirection.Out, Provider = provider!, Urgency = command.Urgency!.Value, Reason = reason, Sections = string.Join(',', sections.Order()),
            State = ReferralState.Draft, CreatedBy = Me, CreatedAt = clock.GetUtcNow(),
        };
        await SaveAsync(referral, CreateAction, null, () => { db.Referrals.Add(referral); db.ReferralEvents.Add(Step(referral.Id, ReferralState.Draft, null, null)); return Task.CompletedTask; }, cancellationToken);
        return await ViewAsync(referral.Id, cancellationToken);
    }

    public async Task<ReferralView?> FileInAsync(Guid facilityId, FileReferralIn command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.PatientId is not { } patientId) throw new ReferralValidationException([("patientId", "required")]);
        if (await facilities.FindAsync(facilityId, cancellationToken) is null) return null;
        if ((await patients.SummariesAsync([patientId], cancellationToken)).Count == 0) return null;
        await clinical.RequireAsync(patientId, facilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        var errors = new List<(string, string)>();
        var provider = Text(command.Provider, "provider", MaxProvider, true, errors);
        var reason = Text(command.Reason, "reason", MaxReason, false, errors);
        if (command.DocumentId is not { } documentId) errors.Add(("documentId", "required"));
        else if (!await documents.BelongsToAsync(documentId, patientId, cancellationToken)) errors.Add(("documentId", "unknown"));
        if (errors.Count > 0) throw new ReferralValidationException(errors);

        var referral = new Referral
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = facilityId, PatientId = patientId, Direction = ReferralDirection.In, Provider = provider!, Urgency = ReferralUrgency.Routine,
            Reason = reason, State = ReferralState.Received, LetterDocumentId = command.DocumentId, CreatedBy = Me, CreatedAt = clock.GetUtcNow(),
        };
        await SaveAsync(referral, FileInAction, null, () =>
        {
            db.Referrals.Add(referral);
            db.ReferralEvents.Add(Step(referral.Id, ReferralState.Received, command.DocumentId, null));
            return Task.CompletedTask;
        }, cancellationToken);
        return await ViewAsync(referral.Id, cancellationToken);
    }

    public async Task<ReferralView?> SendAsync(Guid referralId, SendReferral command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(referralId, cancellationToken) is not { } referral) return null;
        await clinical.RequireAsync(referral.PatientId, referral.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        if (command.Channel is not { } channel || !Enum.IsDefined(channel)) throw new ReferralValidationException([("channel", "required")]);
        if (referral.Direction != ReferralDirection.Out) throw new ReferralConflictException("not_allowed");
        if (referral.State != ReferralState.Draft) throw new ReferralConflictException("already_sent");

        // The consent is asked of the record now: a withdrawal after the draft was written takes effect on this send.
        DateTimeOffset? decided = null;
        if (ReferralRules.NeedsConsent(channel))
        {
            decided = (await consents.CurrentAsync(referral.PatientId, cancellationToken))?
                .FirstOrDefault(c => c.Type == ConsentType.InformationExchange && c.Granted)?.DecidedAt;
            if (decided is null) throw new ReferralConflictException("consent_required");
        }

        var (now, me) = (clock.GetUtcNow(), Me);
        await SaveAsync(referral, SendAction, $"channel:{channel}", async () =>
        {
            var moved = await db.Referrals.Where(r => r.Id == referralId && r.State == ReferralState.Draft).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.State, ReferralState.Sent).SetProperty(r => r.Channel, channel).SetProperty(r => r.SentAt, now).SetProperty(r => r.SentBy, me)
                .SetProperty(r => r.ConsentDecidedAt, decided), cancellationToken);
            if (moved == 0) throw new ReferralConflictException("already_sent");
            db.ReferralEvents.Add(Step(referralId, ReferralState.Sent, null, null));
        }, cancellationToken);
        return await ViewAsync(referralId, cancellationToken);
    }

    public async Task<ReferralView?> AdvanceAsync(Guid referralId, AdvanceReferral command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(referralId, cancellationToken) is not { } referral) return null;
        await clinical.RequireAsync(referral.PatientId, referral.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        var errors = new List<(string, string)>();
        var note = Text(command.Note, "note", MaxNote, false, errors);
        if (command.State is not { } to || !Enum.IsDefined(to)) errors.Add(("state", "required"));
        if (command.DocumentId is { } documentId && !await documents.BelongsToAsync(documentId, referral.PatientId, cancellationToken)) errors.Add(("documentId", "unknown"));
        if (errors.Count > 0) throw new ReferralValidationException(errors);
        var target = command.State!.Value;
        if (!ReferralRules.CanMove(referral.State, target)) throw new ReferralConflictException("not_allowed");

        var from = referral.State;
        await SaveAsync(referral, AdvanceAction, $"state:{target}", async () =>
        {
            if (await db.Referrals.Where(r => r.Id == referralId && r.State == from).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, target), cancellationToken) == 0)
                throw new ReferralConflictException("not_allowed"); // somebody moved it first
            db.ReferralEvents.Add(Step(referralId, target, command.DocumentId, note));
        }, cancellationToken);
        return await ViewAsync(referralId, cancellationToken);
    }

    public async Task<IReadOnlyList<ReferralView>?> ForPatientAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        if ((await patients.SummariesAsync([patientId], cancellationToken)).Count == 0) return null;
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        var rows = await db.Referrals.AsNoTracking().Where(r => r.PatientId == patientId).OrderBy(r => r.State == ReferralState.Closed).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var steps = (await db.ReferralEvents.AsNoTracking().Where(e => ids.Contains(e.ReferralId)).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(cancellationToken)).ToLookup(e => e.ReferralId);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = ReadAction, ResourceType = Resource, PatientId = patientId, FacilityId = facilityId,
            Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
        }, cancellationToken);
        return rows.Select(r => View(r, steps[r.Id])).ToList();
    }

    public async Task<ReferralLetter?> LetterAsync(Guid referralId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(referralId, cancellationToken) is not { } referral) return null;
        await clinical.RequireAsync(referral.PatientId, referral.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        if (referral.Direction != ReferralDirection.Out) return null;
        var patient = (await patients.SummariesAsync([referral.PatientId], cancellationToken)).GetValueOrDefault(referral.PatientId) ?? throw new InvalidOperationException("Patient not in this tenant.");
        var facility = await facilities.FindAsync(referral.FacilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.");
        var doctor = (await staffUsers.DisplayNamesAsync([referral.CreatedBy], cancellationToken)).GetValueOrDefault(referral.CreatedBy);

        // Only what was selected is read at all: a section that was not chosen never enters the letter.
        var wanted = Sections(referral);
        var sections = new List<LetterSection>();
        if (wanted.Count > 0)
        {
            var record = await chart.GetAsync(referral.PatientId, referral.FacilityId, cancellationToken);
            foreach (var s in wanted)
                sections.Add(new LetterSection(s, s switch
                {
                    ReferralSection.Allergies => record.Allergies.Select(a => new LetterLine(a.Reaction is null ? a.Substance : $"{a.Substance}: {a.Reaction}", a.Source.ToString())).ToList(),
                    // Active problems only; a provisional one is marked so, never passed on as a confirmed diagnosis (MDV-P2-010).
                    ReferralSection.Problems => record.Problems.Where(p => p.Status == ProblemStatus.Active)
                        .Select(p => new LetterLine(p.Description, p.Verification == ProblemVerification.Provisional ? nameof(ProblemVerification.Provisional) : p.Source.ToString())).ToList(),
                    _ => record.Medicines.Where(m => m.State == MedicationState.Active).Select(m => new LetterLine(m.Dose is null ? m.Name : $"{m.Name} {m.Dose}", m.Source.ToString())).ToList(),
                }));
        }
        await audit.WriteAsync(Event(LetterAction, referral), cancellationToken);
        return new ReferralLetter(new LetterPatient(patient.FullName, patient.Mrn, patient.DateOfBirth, patient.SexAtBirth.ToString()), facility.Name, doctor, referral.Provider,
            referral.Urgency, referral.Reason, referral.SentAt ?? clock.GetUtcNow(), sections);
    }

    async Task<Referral?> FindAsync(Guid id, CancellationToken cancellationToken) => await db.Referrals.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    async Task<ReferralView> ViewAsync(Guid id, CancellationToken cancellationToken)
    {
        var referral = await db.Referrals.AsNoTracking().SingleAsync(r => r.Id == id, cancellationToken);
        return View(referral, await db.ReferralEvents.AsNoTracking().Where(e => e.ReferralId == id).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(cancellationToken));
    }

    async Task SaveAsync(Referral referral, string action, string? detail, Func<Task> change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, referral, detail), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    ReferralEvent Step(Guid referralId, ReferralState state, Guid? documentId, string? note) =>
        new() { Id = Guid.CreateVersion7(), TenantId = TenantId, ReferralId = referralId, State = state, DocumentId = documentId, Note = note, By = Me, At = clock.GetUtcNow() };

    static List<ReferralSection> Sections(Referral r) =>
        r.Sections.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<ReferralSection>).ToList();

    static ReferralView View(Referral r, IEnumerable<ReferralEvent> history) =>
        new(r.Id, r.PatientId, r.Direction, r.Provider, r.Urgency, r.Reason, Sections(r), r.State, r.Channel, r.SentAt, ReferralRules.IsOpen(r.State), r.LetterDocumentId,
            history.Select(e => new ReferralStep(e.State, e.At, e.DocumentId, e.Note)).ToList());

    static string? Text(string? value, string field, int max, bool required, List<(string, string)> errors)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (v is null && required) errors.Add((field, "required"));
        else if (v?.Length > max) errors.Add((field, "too_long"));
        return v;
    }

    AuditEvent Event(string action, Referral r, string? detail = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = r.Id, PatientId = r.PatientId, FacilityId = r.FacilityId,
        Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), Detail = detail,
    };

    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}
