using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Host.Api.Middleware;

/// <summary>
/// Development-only test seam (MDV-P0-029): the full-stack browser journey starts the clock at a fixed morning so "today's" slots exist
/// whenever the suite runs, then lets it tick. Ignored outside Development, so production time can never be moved by configuration.
/// </summary>
public sealed class DevClock(DateTimeOffset start) : TimeProvider
{
    public const string Setting = "Testing:ClockStartUtc";

    readonly long began = Stopwatch.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => start + Stopwatch.GetElapsedTime(began);

    public static void AddIfDevelopment(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment() || builder.Configuration[Setting] is not { } at) return;
        builder.Services.AddSingleton<TimeProvider>(new DevClock(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture)));
        // Cookies and sign-in state expire in the real browser's time: they keep the real clock.
        builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.TimeProvider = TimeProvider.System);
        builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o => o.TimeProvider = TimeProvider.System);
    }
}
