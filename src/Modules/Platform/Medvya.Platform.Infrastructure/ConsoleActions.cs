using Medvya.Platform.Application;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Platform.Infrastructure;

/// <summary>Platform audit entries for actions through a reviewed path (MDV-P1-064, MDV-P5-064); see <see cref="IConsoleActions"/>.</summary>
sealed class ConsoleActions(PlatformDbContext db, IPlatformCaller caller, TimeProvider clock) : IConsoleActions
{
    public async Task RecordAsync(string action, string resourceType, Guid resourceId, Guid? subjectTenantId, string? reason, string? subjectName = null, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(PlatformAudit.Entry(caller, clock.GetUtcNow(), action, resourceType, resourceId, reason, subjectTenantId: subjectTenantId, subjectName: subjectName, detail: detail));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> RecordedAsync(string action, Guid subjectTenantId, DateTimeOffset? since, CancellationToken cancellationToken = default)
    {
        var member = caller.MemberId ?? throw new InvalidOperationException("No signed-in console member.");
        // The organisation only narrows the read; resource ids are unique, so the member and the time are what the answer rests on.
        return (await db.AuditEntries.AsNoTracking()
            .Where(e => e.ActorMemberId == member && e.Action == action && e.SubjectTenantId == subjectTenantId && e.ResourceId != null && (since == null || e.OccurredAt >= since))
            .Select(e => e.ResourceId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> MemberNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Members.AsNoTracking().Where(m => wanted.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);
    }
}
