using System.Security.Claims;
using Medvya.Platform.Application;

namespace Host.Platform.Context;

/// <summary>The console member, client address and correlation id of the current request, for the platform audit log.</summary>
public sealed class PlatformCaller(IHttpContextAccessor accessor) : IPlatformCaller
{
    public Guid? MemberId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? ClientIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
