namespace Medvya.BuildingBlocks.Domain;

/// <summary>
/// Where the current request came from, for the audit trail: the client address (after trusted forwarded headers) and the
/// request's correlation id. Null outside a request (tests, tools).
/// </summary>
public interface IRequestOrigin
{
    string? ClientIp { get; }
    string? CorrelationId { get; }
}
