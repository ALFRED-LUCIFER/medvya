using System.Data.Common;
using Medvya.Audit.Domain;

namespace Medvya.Audit.Application;

/// <summary>Writes audit events. Implementations must fail loudly: an audited action without its audit record is not allowed.</summary>
public interface IAuditLog
{
    /// <summary>Writes and commits the event on its own: for reads and refusals, where nothing else is saved.</summary>
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the event inside the caller's open transaction on the Medvya database, so it commits with the change it records and
    /// a failed audit write rolls that change back (fail closed). Every audited write uses this (MDV-P0-017).
    /// </summary>
    Task WriteAsync(AuditEvent auditEvent, DbTransaction transaction, CancellationToken cancellationToken = default);
}
