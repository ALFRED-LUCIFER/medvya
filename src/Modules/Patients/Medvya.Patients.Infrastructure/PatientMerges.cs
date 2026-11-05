using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Patients.Domain;
using Medvya.Patients.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Patients.Infrastructure;

/// <summary>
/// Merge review (MDV-P1-017). The merge keeps both records: the merged one is mapped to the survivor (<see cref="Patient.MergedIntoPatientId"/>),
/// its identifiers and visits are moved with a marker of where they came from, and an undo moves them back. Every change and its
/// audit event commit in one transaction. The route for who may do what is the endpoint's capability policy; the rules that make a
/// merge impossible without its approvers are here, so no caller can skip them.
/// shortcut: one facility's records only, the older record always survives, and the merged record's phone, payer, contacts and
/// consents stay on it (history keeps them); a survivor choice and field-by-field merge wait for pilot feedback.
/// </summary>
public sealed class PatientMerges(PatientsDbContext db, ICurrentTenant tenant, ICurrentFacility facility, ICurrentUser user, IAuditLog audit, TimeProvider clock,
    IEnumerable<IPatientMergeParticipant> mergeParticipants) : IPatientMerges
{
    const string Purpose = "registration";
    readonly IReadOnlyList<IPatientMergeParticipant> participants = [.. mergeParticipants];

    static readonly MergeRequestStatus[] Open = [MergeRequestStatus.Proposed, MergeRequestStatus.AwaitingApproval];

    Guid FacilityId() => facility.FacilityId ?? throw new InvalidOperationException("No facility context.");

    Guid TenantId() => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    /// <summary>Requests whose two records are both at the active facility (a request is never visible through another facility).</summary>
    IQueryable<MergeRequest> Requests()
    {
        var facilityId = FacilityId();
        return db.MergeRequests.Where(r => db.Patients.Any(p => p.Id == r.PatientId && p.FacilityId == facilityId)
            && db.Patients.Any(p => p.Id == r.CandidatePatientId && p.FacilityId == facilityId));
    }

    public async Task<IReadOnlyList<MergeQueueRow>> QueueAsync(CancellationToken cancellationToken = default) =>
        await Requests().AsNoTracking().Where(r => Open.Contains(r.Status)).OrderByDescending(r => r.DecidedAt)
            .Select(r => new MergeQueueRow(r.Id, r.Status,
                db.Patients.Where(p => p.Id == r.CandidatePatientId).Select(p => p.Mrn).First(),
                db.Patients.Where(p => p.Id == r.PatientId).Select(p => p.Mrn).First(), r.DecidedAt))
            .ToListAsync(cancellationToken);

    public async Task<MergeReview?> GetAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await Requests().AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return null;
        var required = await ApprovalsRequiredAsync(request, cancellationToken);
        var approvals = (request.FirstApprovedBy is null ? 0 : 1) + (request.SecondApprovedBy is null ? 0 : 1);
        var canUndo = request.Status == MergeRequestStatus.Merged && clock.GetUtcNow() - request.MergedAt <= PatientMerge.UndoWindow;
        return new(request.Id, request.Status, await SideAsync(request.CandidatePatientId, cancellationToken), await SideAsync(request.PatientId, cancellationToken),
            required, approvals, canUndo);
    }

    async Task<MergeSide> SideAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var p = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == patientId, cancellationToken);
        var ids = await db.Identifiers.AsNoTracking().Where(i => i.PatientId == patientId || i.MovedFromPatientId == patientId).ToListAsync(cancellationToken);
        var visits = 0;
        foreach (var part in participants) visits += await part.VisitsAsync(patientId, cancellationToken);
        return new(p.Id, p.FullName, p.Mrn, p.DateOfBirth, p.SexAtBirth, p.Mobile,
            [.. ids.OrderBy(i => i.Scheme).Select(i => new MaskedIdentifier(i.Scheme, Tail(i.LookupValue ?? i.DisplayValue ?? "")))], visits);
    }

    static string Tail(string value) => value.Length <= 4 ? value : value[^4..];

    public async Task<Guid?> RequestAsync(Guid patientId, Guid otherPatientId, CancellationToken cancellationToken = default)
    {
        var facilityId = FacilityId();
        if (patientId == otherPatientId) throw Refused("same_record");
        var pair = await db.Patients.Where(p => (p.Id == patientId || p.Id == otherPatientId) && p.FacilityId == facilityId).ToListAsync(cancellationToken);
        if (pair.Count != 2) return null;
        if (pair.Any(p => p.MergedIntoPatientId is not null)) throw Refused("already_merged");
        // Suppression: any earlier request for the pair, in either order, wins ("not the same" is final; a merged or undone pair is not re-asked).
        if (await db.MergeRequests.AnyAsync(r => (r.PatientId == patientId && r.CandidatePatientId == otherPatientId)
            || (r.PatientId == otherPatientId && r.CandidatePatientId == patientId), cancellationToken))
            throw Refused("pair_decided");

        var survivor = pair.OrderBy(p => p.RegisteredAt).ThenBy(p => p.Id).First();
        var merged = pair.Single(p => p.Id != survivor.Id);
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = new MergeRequest
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId(), PatientId = merged.Id, CandidatePatientId = survivor.Id,
            Status = MergeRequestStatus.Proposed, DecidedBy = user.UserId, DecidedAt = now,
        };
        db.MergeRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(tx, "patient.merge.request", survivor.Id, request.Id, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return request.Id;
    }

    public async Task<bool> NotSameAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await Requests().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return false;
        if (!Open.Contains(request.Status)) throw Refused("not_open");
        await DecideAsync(request, MergeRequestStatus.NotSame, "patient.merge.not_same", cancellationToken);
        return true;
    }

    public async Task<bool> RequestApprovalAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await Requests().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return false;
        if (request.Status != MergeRequestStatus.Proposed) throw Refused("not_proposed");
        await DecideAsync(request, MergeRequestStatus.AwaitingApproval, "patient.merge.request_approval", cancellationToken);
        return true;
    }

    async Task DecideAsync(MergeRequest request, MergeRequestStatus status, string action, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        request.Status = status;
        request.DecidedBy = user.UserId;
        request.DecidedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(tx, action, request.CandidatePatientId, request.Id, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<MergeRequestStatus?> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await Requests().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return null;
        if (request.Status != MergeRequestStatus.AwaitingApproval) throw Refused("not_awaiting_approval");
        var me = user.UserId ?? throw new InvalidOperationException("No user context.");
        // The same person never counts twice: a second approver is a different clinician.
        if (request.FirstApprovedBy == me) throw Refused("same_approver");

        var required = await ApprovalsRequiredAsync(request, cancellationToken);
        var now = clock.GetUtcNow();
        var patients = await db.Patients.Where(p => p.Id == request.PatientId || p.Id == request.CandidatePatientId).ToListAsync(cancellationToken);
        var merged = patients.Single(p => p.Id == request.PatientId);
        var survivor = patients.Single(p => p.Id == request.CandidatePatientId);
        // A record that already absorbed others, or was absorbed, cannot take part: undo the earlier merge first (keeps every undo exact).
        if (merged.MergedIntoPatientId is not null || survivor.MergedIntoPatientId is not null
            || await db.Patients.AnyAsync(p => p.MergedIntoPatientId == merged.Id, cancellationToken))
            throw Refused("record_in_merge");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (request.FirstApprovedBy is null) request.FirstApprovedBy = me; else request.SecondApprovedBy = me;
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(tx, "patient.merge.approve", survivor.Id, request.Id, now, cancellationToken);

        // The merge itself runs only here, and only with every required approval in place.
        if ((request.FirstApprovedBy is null ? 0 : 1) + (request.SecondApprovedBy is null ? 0 : 1) >= required)
        {
            var onSurvivor = (await db.Identifiers.Where(i => i.PatientId == survivor.Id).ToListAsync(cancellationToken)).Select(i => (i.Scheme, Value: i.LookupValue ?? i.DisplayValue)).ToHashSet();
            // An identifier the survivor already holds stays on the merged record; the survivor keeps one copy.
            foreach (var id in await db.Identifiers.Where(i => i.PatientId == merged.Id).ToListAsync(cancellationToken))
                if (!onSurvivor.Contains((id.Scheme, id.LookupValue ?? id.DisplayValue))) { id.PatientId = survivor.Id; id.MovedFromPatientId = merged.Id; }
            foreach (var part in participants) await part.MoveAsync(merged.Id, survivor.Id, tx.GetDbTransaction(), cancellationToken);
            merged.MergedIntoPatientId = survivor.Id;
            request.Status = MergeRequestStatus.Merged;
            request.MergedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(tx, "patient.merge", survivor.Id, request.Id, now, cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        return request.Status;
    }

    /// <summary>Two when both records hold clinical data, otherwise one.</summary>
    async Task<int> ApprovalsRequiredAsync(MergeRequest request, CancellationToken cancellationToken)
    {
        var both = true;
        foreach (var id in new[] { request.PatientId, request.CandidatePatientId })
        {
            var has = false;
            foreach (var part in participants) has |= await part.HasClinicalDataAsync(id, cancellationToken);
            both &= has;
        }
        return both ? 2 : 1;
    }

    public async Task<bool> UndoAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await Requests().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return false;
        var now = clock.GetUtcNow();
        if (request.Status != MergeRequestStatus.Merged) throw Refused("not_merged");
        if (now - request.MergedAt > PatientMerge.UndoWindow) throw Refused("undo_window_over");

        var merged = await db.Patients.SingleAsync(p => p.Id == request.PatientId, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var id in await db.Identifiers.Where(i => i.PatientId == request.CandidatePatientId && i.MovedFromPatientId == merged.Id).ToListAsync(cancellationToken))
        {
            id.PatientId = merged.Id;
            id.MovedFromPatientId = null;
        }
        foreach (var part in participants) await part.RestoreAsync(merged.Id, request.CandidatePatientId, tx.GetDbTransaction(), cancellationToken);
        merged.MergedIntoPatientId = null;
        request.Status = MergeRequestStatus.Unmerged;
        request.UnmergedBy = user.UserId;
        request.UnmergedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(tx, "patient.merge.undo", request.CandidatePatientId, request.Id, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MergeHistoryEntry>?> HistoryAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var facilityId = FacilityId();
        if (!await db.Patients.AnyAsync(p => p.Id == patientId && p.FacilityId == facilityId, cancellationToken)) return null;
        var rows = await Requests().AsNoTracking()
            .Where(r => (r.PatientId == patientId || r.CandidatePatientId == patientId) && (r.Status == MergeRequestStatus.Merged || r.Status == MergeRequestStatus.Unmerged))
            .OrderByDescending(r => r.MergedAt)
            .Select(r => new MergeHistoryEntry(r.Id, r.Status, r.CandidatePatientId == patientId,
                db.Patients.Where(p => p.Id == (r.CandidatePatientId == patientId ? r.PatientId : r.CandidatePatientId)).Select(p => p.Mrn).First(), r.MergedAt, r.UnmergedAt))
            .ToListAsync(cancellationToken);
        return rows;
    }

    static PatientValidationException Refused(string code) => new([new("merge", code)]);

    // In the change's own transaction: the event commits or rolls back with it. Opaque ids only.
    Task AuditAsync(IDbContextTransaction tx, string action, Guid patientId, Guid requestId, DateTimeOffset now, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId(), ActorUserId = user.UserId, Action = action, ResourceType = "merge_request",
            ResourceId = requestId, PatientId = patientId, Purpose = Purpose, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
}
