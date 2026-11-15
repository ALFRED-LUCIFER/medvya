using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Medvya.BuildingBlocks.Security;

/// <summary>
/// The general request limit of both hosts (#1365). A clinic's staff share one internet address, so a signed-in request is counted against
/// its user and only a request without a session against the client address:
/// - an endpoint that needs no sign-in (public pages, sign-in, health) or matches nothing is counted per address by UseRateLimiter, before
///   authentication, so the public site never reads the session cookie;
/// - an endpoint that needs sign-in is counted by <see cref="UseSignedInRateLimit"/> after authentication: per user, or per address when the
///   session turns out to be missing or invalid, so a made-up cookie buys nothing.
/// Static files are served before UseRateLimiter and are not counted. Each partition gets the same number of requests a minute.
/// shortcut: counted in memory per app instance, so scaling out multiplies the limits; move to a shared store before a second instance.
/// </summary>
public static class GeneralRateLimit
{
    const string Deferred = "medvya.ratelimit.deferred";

    public static void AddGeneralLimit(this RateLimiterOptions options, int permitsPerMinute)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (rejected, _) => new ValueTask(WriteRejectionAsync(rejected.HttpContext, rejected.Lease));
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            var metadata = ctx.GetEndpoint()?.Metadata;
            if (!ctx.Items.ContainsKey(Deferred) && metadata?.GetMetadata<IAuthorizeData>() is not null && metadata.GetMetadata<IAllowAnonymous>() is null)
            {
                ctx.Items[Deferred] = true;
                return RateLimitPartition.GetNoLimiter("deferred");
            }
            var key = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } user ? $"user:{user}" : $"address:{ctx.Connection.RemoteIpAddress}";
            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) });
        });
    }

    /// <summary>Call between authentication and authorization (a refused request is counted too): counts the requests <see cref="AddGeneralLimit"/> left for it, now that the user is known.</summary>
    public static IApplicationBuilder UseSignedInRateLimit(this IApplicationBuilder app)
    {
        var limiter = app.ApplicationServices.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter!;
        return app.Use(async (ctx, next) =>
        {
            if (ctx.Items.TryGetValue(Deferred, out var deferred) && deferred is true)
            {
                ctx.Items[Deferred] = false; // decided now: the partition is the user's or the address's
                using var lease = await limiter.AcquireAsync(ctx, 1, ctx.RequestAborted);
                if (!lease.IsAcquired)
                {
                    await WriteRejectionAsync(ctx, lease);
                    return;
                }
            }
            await next(ctx);
        });
    }

    /// <summary>429 with Retry-After and a small problem body, so a client can wait and a person reads a reason, not a blank page.</summary>
    public static Task WriteRejectionAsync(HttpContext http, RateLimitLease lease)
    {
        var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : 60;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        return http.Response.WriteAsJsonAsync(new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests", Detail = $"Try again in {seconds} seconds." },
            options: null, contentType: "application/problem+json", http.RequestAborted);
    }
}
