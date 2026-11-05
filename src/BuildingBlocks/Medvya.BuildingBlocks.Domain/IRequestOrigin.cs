namespace Medvya.BuildingBlocks.Domain;

/// <summary>
/// Where the current request came from, for the audit trail: the client address (after trusted forwarded headers) and the
/// request's correlation id. Null outside a request (tests, tools).
/// </summary>
public interface IRequestOrigin
{
    string? ClientIp { get; }
    string? CorrelationId { get; }
    /// <summary>The role of the signed-in person's active membership at the session's facility, read from the database (never a claim), kept on audit events so the log shows the authority held at the time.</summary>
    Task<string?> RoleAsync(CancellationToken cancellationToken = default);
    /// <summary>The facility the session works at.</summary>
    Guid? FacilityId { get; }
}
