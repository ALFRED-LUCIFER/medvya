using Medvya.Platform.Application;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Platform.Infrastructure;

/// <summary>Platform audit entries for organisation actions (MDV-P1-064); see <see cref="IConsoleActions"/>.</summary>
sealed class ConsoleActions(PlatformDbContext db, IPlatformCaller caller, TimeProvider clock) : IConsoleActions
{
    public async Task RecordAsync(string action, string resourceType, Guid resourceId, Guid subjectTenantId, string? reason, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(PlatformAudit.Entry(caller, clock.GetUtcNow(), action, resourceType, resourceId, reason, subjectTenantId: subjectTenantId));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> MemberNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Members.AsNoTracking().Where(m => wanted.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken);
    }
}
