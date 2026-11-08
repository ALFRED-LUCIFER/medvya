using Microsoft.AspNetCore.Builder;

namespace Medvya.BuildingBlocks.Security;

/// <summary>OWASP ASVS V14.4 response headers on every response (MDV-P0-026); shared by the API and the platform console host.</summary>
public static class SecurityHeaders
{
    // style-src needs 'unsafe-inline' because MUI/Emotion injects <style> tags; scripts stay strict.
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <param name="imageOrigin">Origin of the private Blob endpoint, so signed logo links load (img-src); null when no storage is configured.</param>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string? imageOrigin = null) => app.Use(async (ctx, next) =>
    {
        var policy = imageOrigin is null ? ContentSecurityPolicy : ContentSecurityPolicy.Replace("img-src 'self' data: blob:", $"img-src 'self' data: blob: {imageOrigin}", StringComparison.Ordinal);
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h.ContentSecurityPolicy = policy;
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
