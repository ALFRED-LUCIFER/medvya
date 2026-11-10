using System.Globalization;
using System.Text;
using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Platform.Infrastructure;

/// <summary>Team access (MDV-P5-040). Every change and its audit entry are saved together, so no change goes unlogged.</summary>
sealed class PlatformTeam(PlatformDbContext db, IPlatformCaller caller, TimeProvider clock) : IPlatformTeam
{
    public static readonly TimeSpan AccessReviewInterval = TimeSpan.FromDays(90);
    const string AccessReview = "team.access_review";

    Guid Actor => caller.MemberId ?? throw new InvalidOperationException("No signed-in console member.");

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken = default) =>
        await db.Members.AsNoTracking().Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);

    public async Task<TeamView> ListAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var members = await db.Members.AsNoTracking().Where(m => m.RemovedAt == null).OrderBy(m => m.DisplayName).ToListAsync(cancellationToken);
        var withKeys = await db.Keys.AsNoTracking().Where(k => k.RevokedAt == null).Select(k => k.MemberId).Distinct().ToListAsync(cancellationToken);
        var lastReview = await db.AuditEntries.AsNoTracking().Where(a => a.Action == AccessReview).MaxAsync(a => (DateTimeOffset?)a.OccurredAt, cancellationToken);
        return new TeamView(
            [.. members.Select(m => new TeamMemberView(m.Id, m.DisplayName, m.Email, m.Role, withKeys.Contains(m.Id), m.Subject is not null, m.LastActiveAt, m.AccessEndsOn))],
            lastReview, lastReview is { } r ? r + AccessReviewInterval : now);
    }

    public async Task<IssuedLink> InviteAsync(Invite invite, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var name = invite.DisplayName?.Trim();
        var email = invite.Email?.Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(name)) errors["displayName"] = ["required"];
        else if (name.Length > PlatformMember.NameMax) errors["displayName"] = ["too_long"];
        if (string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal) || email.Length > PlatformMember.EmailMax) errors["email"] = ["invalid"];
        else if (await db.Members.AnyAsync(m => m.RemovedAt == null && m.Email == email, cancellationToken)) errors["email"] = ["taken"];
        if (!Enum.IsDefined(invite.Role)) errors["role"] = ["invalid"];
        if (invite.AccessEndsOn is { } ends && ends < DateOnly.FromDateTime(now.UtcDateTime)) errors["accessEndsOn"] = ["past"];
        PlatformValidationException.ThrowIfAny(errors);

        var member = new PlatformMember { DisplayName = name!, Email = email!, Role = invite.Role, AccessEndsOn = invite.AccessEndsOn, InvitedAt = now, InvitedBy = Actor };
        var (link, token) = PlatformAudit.NewLink(member.Id, KeyLinkPurpose.Invitation, Actor, now);
        db.Members.Add(member);
        db.Links.Add(link);
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "team.invite", "platform_member", member.Id, invite.Reason));
        await db.SaveChangesAsync(cancellationToken);
        return new IssuedLink(member.Id, token, link.ExpiresAt);
    }

    public async Task<(TeamOutcome Outcome, IssuedLink? Link)> IssueRecoveryAsync(Guid memberId, string reason, CancellationToken cancellationToken = default)
    {
        // Another owner verifies the person on a video call: nobody recovers their own key.
        if (memberId == Actor) return (TeamOutcome.Refused, null);
        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == memberId && m.RemovedAt == null, cancellationToken);
        if (member is null) return (TeamOutcome.NotFound, null);
        var now = clock.GetUtcNow();
        // The lost key may be in someone else's hands: it stops working now, not when the new one is registered.
        await EndAccessAsync(memberId, now, cancellationToken);
        var (link, token) = PlatformAudit.NewLink(memberId, KeyLinkPurpose.Recovery, Actor, now);
        db.Links.Add(link);
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "key.recovery.issue", "platform_member", memberId, reason));
        await db.SaveChangesAsync(cancellationToken);
        return (TeamOutcome.Done, new IssuedLink(memberId, token, link.ExpiresAt));
    }

    public async Task<TeamOutcome> RemoveAsync(Guid memberId, string reason, CancellationToken cancellationToken = default)
    {
        if (memberId == Actor) return TeamOutcome.Refused;
        // The console always keeps an owner who can invite and recover. The current owners' rows stay locked until commit, so two owners
        // removing each other at once queue: the second sees one owner left and is refused.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM platform."Members" WHERE "Role" = 'Owner' AND "RemovedAt" IS NULL FOR UPDATE""", cancellationToken);
        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == memberId && m.RemovedAt == null, cancellationToken);
        if (member is null) return TeamOutcome.NotFound;
        if (member.Role == PlatformRole.Owner && await db.Members.CountAsync(m => m.RemovedAt == null && m.Role == PlatformRole.Owner, cancellationToken) <= 1)
            return TeamOutcome.Refused;
        var now = clock.GetUtcNow();
        member.RemovedAt = now;
        await EndAccessAsync(memberId, now, cancellationToken);
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "team.remove", "platform_member", memberId, reason));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return TeamOutcome.Done;
    }

    public async Task RecordAccessReviewAsync(string reason, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(PlatformAudit.Entry(caller, clock.GetUtcNow(), AccessReview, "platform_team", null, reason));
        await db.SaveChangesAsync(cancellationToken);
    }

    // Keys revoked, sessions ended and unused links voided: nothing the member held before still works.
    async Task EndAccessAsync(Guid memberId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var key in await db.Keys.Where(k => k.MemberId == memberId && k.RevokedAt == null).ToListAsync(cancellationToken)) key.RevokedAt = now;
        foreach (var s in await db.Sessions.Where(s => s.MemberId == memberId && s.RevokedAt == null).ToListAsync(cancellationToken)) s.RevokedAt = now;
        foreach (var l in await db.Links.Where(l => l.MemberId == memberId && l.UsedAt == null && l.ExpiresAt > now).ToListAsync(cancellationToken)) l.UsedAt = now;
    }
}

/// <summary>The platform audit log: append-only in the database (UPDATE and DELETE are refused for every role), exportable as CSV.</summary>
sealed class PlatformAuditLog(PlatformDbContext db, IPlatformCaller caller, TimeProvider clock) : IPlatformAuditLog
{
    public const int MaxPage = 100;

    public async Task<IReadOnlyList<AuditEntryView>> ListAsync(DateTimeOffset? before, Guid? beforeId, int limit, CancellationToken cancellationToken = default)
    {
        var q = db.AuditEntries.AsNoTracking();
        // The cursor is (time, id), the sort order: entries saved together share a time, and a time alone would skip one at a page boundary.
        if (before is { } b) q = beforeId is { } id ? q.Where(a => a.OccurredAt < b || (a.OccurredAt == b && a.Id.CompareTo(id) < 0)) : q.Where(a => a.OccurredAt < b);
        return await View(q.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(Math.Clamp(limit, 1, MaxPage))).ToListAsync(cancellationToken);
    }

    public async Task RecordAsync(string action, string resourceType, Guid? subjectTenantId, string reason, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(PlatformAudit.Entry(caller, clock.GetUtcNow(), action, resourceType, subjectTenantId, reason, subjectTenantId: subjectTenantId));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ExportCsvAsync(string reason, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(PlatformAudit.Entry(caller, clock.GetUtcNow(), "audit.export", "platform_audit", null, reason));
        await db.SaveChangesAsync(cancellationToken); // logged before anything is handed out
        var csv = new StringBuilder("occurred_at,action,resource_type,resource_id,practice_id,actor,reason\r\n");
        foreach (var a in await View(db.AuditEntries.AsNoTracking().OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)).ToListAsync(cancellationToken))
            csv.AppendJoin(',', [a.OccurredAt.ToString("O", CultureInfo.InvariantCulture), Cell(a.Action), Cell(a.ResourceType), a.ResourceId?.ToString() ?? "",
                a.SubjectTenantId?.ToString() ?? "", Cell(a.Actor), Cell(a.Reason)]).Append("\r\n");
        return csv.ToString();
    }

    IQueryable<AuditEntryView> View(IQueryable<PlatformAuditEntry> q) =>
        from a in q
        join m in db.Members on a.ActorMemberId equals m.Id into actors
        from m in actors.DefaultIfEmpty()
        select new AuditEntryView(a.Id, a.OccurredAt, a.Action, a.ResourceType, a.ResourceId, a.SubjectTenantId, m == null ? null : m.DisplayName, a.Reason);

    /// <summary>
    /// One quoted CSV cell. A cell a spreadsheet would run as a formula (=, +, -, @, tab, carriage return) is prefixed with an apostrophe,
    /// since reasons are free text typed by people.
    /// </summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
