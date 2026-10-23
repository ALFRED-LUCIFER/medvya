using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Api;

/// <summary>
/// Rate limits per signed-in user, enforced as an endpoint filter. The host's rate-limiting middleware runs before
/// authentication (the public site must be limited per IP and must never read the session cookie), so a per-user policy
/// there would see every caller as anonymous and one desk would use up the limit for everyone. Endpoint filters run after
/// authentication and authorization, where the user is known.
/// </summary>
public static class UserRateLimit
{
    /// <summary>Registers the named limit: <paramref name="permitsPerMinute"/> requests per user per minute.</summary>
    public static IServiceCollection AddUserRateLimit(this IServiceCollection services, string name, int permitsPerMinute) =>
        services.AddKeyedSingleton(name, (_, _) => PartitionedRateLimiter.Create<string, string>(user =>
            RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) })));

    /// <summary>
    /// Applies the named per-user limit (429 when used up). Add it before <c>.Audited(...)</c> so a refused request is not
    /// counted as an access. Not registered = the request fails (fail closed).
    /// </summary>
    public static TBuilder RequireUserRateLimit<TBuilder>(this TBuilder builder, string name) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (http.User.FindFirstValue(ClaimTypes.NameIdentifier) is not { } user) return Results.Unauthorized();
            using var lease = await http.RequestServices.GetRequiredKeyedService<PartitionedRateLimiter<string>>(name).AcquireAsync(user, 1, http.RequestAborted);
            return lease.IsAcquired ? await next(ctx) : Results.StatusCode(StatusCodes.Status429TooManyRequests);
        });
}
