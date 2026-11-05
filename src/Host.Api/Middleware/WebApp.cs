using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Host.Api.Middleware;

/// <summary>
/// The built clinic web app (web/clinic, base /app/) served from wwwroot/app by the API host (MDV-P0-007): same origin as the BFF, so the
/// strict session cookie needs no CORS. The deploy copies the build there; where nothing is copied (development, tests) nothing is served.
/// </summary>
public static class WebApp
{
    public const string Path = "/app";

    public static WebApplication UseWebApp(this WebApplication app)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                // Hashed build files never change; the shell page must always be re-read so a deploy takes effect.
                var immutable = ctx.Context.Request.Path.StartsWithSegments($"{Path}/assets", StringComparison.Ordinal);
                ctx.Context.Response.Headers[HeaderNames.CacheControl] = immutable ? "public, max-age=31536000, immutable" : "no-cache";
            },
        });
        // Client-side routes (/app/schedule, /app/invite/…) reload into the shell; a missing file (has an extension) stays a 404.
        // The shell is public by nature: every data call behind it is authorised on the server.
        app.MapFallbackToFile($"{Path}/{{*path:nonfile}}", "app/index.html", new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
        }).AllowAnonymous();
        return app;
    }
}
