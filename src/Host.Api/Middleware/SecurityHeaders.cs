namespace Host.Api.Middleware;

/// <summary>OWASP ASVS V14.4 response headers on every response (MDV-P0-026).</summary>
public static class SecurityHeaders
{
    // style-src needs 'unsafe-inline' because MUI/Emotion injects <style> tags; scripts stay strict.
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.ContentSecurityPolicy = ContentSecurityPolicy;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            // Microphone stays allowed for this origin: consented consultation capture (P3).
            h["Permissions-Policy"] = "camera=(), geolocation=(), payment=(), usb=(), microphone=(self)";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        await next();
    });
}
