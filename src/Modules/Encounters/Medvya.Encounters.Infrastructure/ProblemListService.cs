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
/// The problem list in a consultation (MDV-P2-010). Clinical access first (a doctor, care relationship, the encounter's facility), then the work
/// with its audit event in one transaction. A code must be in the country's code set (<see cref="DiagnosisCodeRules"/>), and the problem keeps
/// the system and version it was coded from; input is validated here, never only in the screen.
/// </summary>
public sealed class ProblemListService(
    EncountersDbContext db, IClinicalAccess clinical, IStaffAccess staff, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IProblemList
{
    public const string ReadAction = "problem.list.read", AddAction = "problem.add", UpdateAction = "problem.update", Resource = "encounter";
    public const int MaxDescription = 200;
    static readonly Access[] Doctors = [Access.Full];

    public async Task<DiagnosisCodeSet> CodeSetAsync(CancellationToken cancellationToken = default) => await CodeSetForTenantAsync(cancellationToken);

    public async Task<ProblemListView?> GetAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ReadAction, encounter), cancellationToken);
        return await ViewAsync(encounter, cancellationToken);
    }

    public async Task<ProblemListView?> AddAsync(Guid encounterId, AddProblem command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var set = await CodeSetForTenantAsync(cancellationToken);
        var errors = new List<(string, string)>();
        DiagnosisCode? code = null;
        if (command.Code is { } raw && !string.IsNullOrWhiteSpace(raw) && (code = set.Find(raw)) is null) errors.Add(("code", "unknown"));
        // The doctor's own wording wins; a code chosen without wording takes the code's description. Neither: nothing to record.
        var description = command.Description?.Trim() is { Length: > 0 } d ? d : code?.Title;
        if (description is null) errors.Add(("description", "required"));
        else if (description.Length > MaxDescription) errors.Add(("description", "too_long"));
        if (command.Verification is not { } verification || !Enum.IsDefined(verification)) errors.Add(("verification", "required"));
        if (Future(command.OnsetOn)) errors.Add(("onsetOn", "future"));
        if (errors.Count > 0) throw new ProblemValidationException(errors);
        if (code is not null && await ActiveElsewhereAsync(encounter.PatientId, code.Code, null, cancellationToken)) throw new ProblemConflictException("duplicate");

        var now = clock.GetUtcNow();
        await SaveAsync(AddAction, encounter, () => db.Problems.Add(new Problem
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = encounter.PatientId, EncounterId = encounter.Id,
            Icd10Code = code?.Code, CodeSystem = code is null ? null : set.System, CodeSystemVersion = code is null ? null : set.Version,
            Description = description!, Verification = command.Verification!.Value, Status = ProblemStatus.Active, OnsetOn = command.OnsetOn,
            Source = command.Verification == ProblemVerification.PatientReported ? ChartSource.PatientReported : ChartSource.ClinicianVerified,
            RecordedAt = now, RecordedBy = Me,
        }), cancellationToken);
        return await ViewAsync(encounter, cancellationToken);
    }

    public async Task<ProblemListView?> UpdateAsync(Guid encounterId, Guid problemId, UpdateProblem command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        // Only a problem of this encounter's patient: another patient's id is "not found", the same as a random one.
        if (await db.Problems.SingleOrDefaultAsync(p => p.Id == problemId && p.PatientId == encounter.PatientId, cancellationToken) is not { } problem) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        if (command.Verification is not { } verification || !Enum.IsDefined(verification)) errors.Add(("verification", "required"));
        if (command.Status is not { } status || !Enum.IsDefined(status)) errors.Add(("status", "required"));
        if (Future(command.OnsetOn)) errors.Add(("onsetOn", "future"));
        if (errors.Count > 0) throw new ProblemValidationException(errors);
        if (command.Status == ProblemStatus.Active && problem.Icd10Code is { } same && await ActiveElsewhereAsync(problem.PatientId, same, problem.Id, cancellationToken))
            throw new ProblemConflictException("duplicate"); // reopening a code that was added again meanwhile

        await SaveAsync(UpdateAction, encounter, () =>
        {
            (problem.Verification, problem.Status, problem.OnsetOn) = (command.Verification!.Value, command.Status!.Value, command.OnsetOn);
            (problem.UpdatedBy, problem.UpdatedAt) = (Me, clock.GetUtcNow());
        }, cancellationToken);
        return await ViewAsync(encounter, cancellationToken);
    }

    // shortcut: a check, then a write; two tabs adding the same code in the same instant both succeed. No unique index fits (resolved
    // copies are allowed); add a partial unique index on active codes if duplicates show up in practice.
    Task<bool> ActiveElsewhereAsync(Guid patientId, string code, Guid? except, CancellationToken cancellationToken) =>
        db.Problems.AnyAsync(p => p.PatientId == patientId && p.Icd10Code == code && p.Status == ProblemStatus.Active && p.Id != except, cancellationToken);

    async Task SaveAsync(string action, Encounter encounter, Action change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, encounter), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    // A day of slack: the server's UTC date can be a day behind the clinic's (India and the UAE are ahead of UTC).
    bool Future(DateOnly? onset) => onset > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);

    async Task<Encounter?> FindAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.Patient360Chart, Doctors, cancellationToken);
        return encounter;
    }

    static void Open(Encounter encounter)
    {
        if (encounter.Status == EncounterStatus.Ended) throw new ProblemConflictException("ended");
    }

    async Task<ProblemListView> ViewAsync(Encounter encounter, CancellationToken cancellationToken)
    {
        var rows = await db.Problems.AsNoTracking().Where(p => p.PatientId == encounter.PatientId)
            .OrderBy(p => p.Status).ThenBy(p => p.RecordedAt).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return new ProblemListView(encounter.Id, encounter.PatientId, [.. rows.Select(p => new ProblemRow(
            p.Id, p.Icd10Code, p.CodeSystem, p.CodeSystemVersion, p.Description, p.Verification, p.Status, p.OnsetOn, p.Source,
            p.EncounterId == encounter.Id, p.Verification == ProblemVerification.Verified, p.RecordedAt))], encounter.Status == EncounterStatus.Ended);
    }

    async Task<DiagnosisCodeSet> CodeSetForTenantAsync(CancellationToken cancellationToken) =>
        DiagnosisCodeRules.For(await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.")).CodeSet();

    AuditEvent Event(string action, Encounter e) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = e.Id,
        PatientId = e.PatientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");
}
