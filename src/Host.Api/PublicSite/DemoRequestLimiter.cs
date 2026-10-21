using System.Threading.RateLimiting;

namespace Host.Api.PublicSite;

/// <summary>
/// Demo requests per client IP per hour (MDV-P1-003). Checked in the page handler rather than as an endpoint policy, so only a
/// submission counts (not reading the trust centre) and a refused visitor gets the form back with a message, not a bare 429.
/// </summary>
public sealed class DemoRequestLimiter(int permitsPerHour) : IDisposable
{
    readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(ip =>
        RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerHour, Window = TimeSpan.FromHours(1) }));

    public bool TryAcquire(HttpContext ctx)
    {
        using var lease = limiter.AttemptAcquire(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
