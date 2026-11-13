using Medvya.Audit.Domain;

namespace Medvya.Audit.Application;

/// <summary>Writes an event with no tenant into the platform chain (<see cref="PlatformTenant"/>), on its own commit; the event's tenant id is ignored. Fails loudly like <see cref="IAuditLog"/>.</summary>
public interface IPlatformAuditLog
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
