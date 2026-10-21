using Medvya.Audit.Domain;

namespace Medvya.Audit.Application;

/// <summary>Writes audit events. Implementations must fail loudly: an audited action without its audit record is not allowed.</summary>
public interface IAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
