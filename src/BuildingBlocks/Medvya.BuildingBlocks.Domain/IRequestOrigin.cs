namespace Medvya.BuildingBlocks.Domain;

/// <summary>
/// Where the current request came from, for the audit trail: the client address (after trusted forwarded headers) and the
/// request's correlation id. Null outside a request (tests, tools).
/// </summary>
public interface IRequestOrigin
{
    string? ClientIp { get; }
    string? CorrelationId { get; }
    /// <summary>The role the signed-in person holds in this session (the session's role claim), kept on audit events so the log shows the authority held at the time.</summary>
    string? Role { get; }
    /// <summary>The facility the session works at.</summary>
    Guid? FacilityId { get; }
}
