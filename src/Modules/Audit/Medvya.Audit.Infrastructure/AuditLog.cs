using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure.Persistence;

namespace Medvya.Audit.Infrastructure;

public sealed class AuditLog(AuditDbContext db) : IAuditLog
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        db.Events.Add(auditEvent);
        await db.SaveChangesAsync(cancellationToken); // throws on failure: the caller's request must not succeed unaudited
    }
}
