using Medvya.BuildingBlocks.Domain;

namespace Medvya.Identity.Domain;

/// <summary>
/// One signed-in browser (ADR-004, MDV-P0-012). The cookie carries only its id; this row decides on every request whether
/// the session still lives, so idle timeout, the 8 h cap and revocation hold even for a stolen cookie.
/// shortcut: ended rows are kept forever; purge them (e.g. after 90 days) when the data-retention jobs exist.
/// </summary>
public sealed class StaffSession
{
    /// <summary>Hard cap from sign-in, whatever the activity (ADR-004).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);
    /// <summary>Idle timeout before a facility is chosen; afterwards the tenant's setting applies.</summary>
    public const int DefaultIdleTimeoutMinutes = 15;
    /// <summary>Native app sessions (MDV-P5-033): the app locks itself after 15 idle minutes (biometric/PIN), so the server-side caps are longer.</summary>
    public static readonly TimeSpan NativeMaxLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan NativeIdleTimeout = TimeSpan.FromDays(7);

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid UserId { get; init; }
    /// <summary>Tenant and membership the session is scoped to; null until a facility is chosen.</summary>
    [Classification(DataClass.Internal)] public Guid? TenantId { get; set; }
    [Classification(DataClass.Internal)] public Guid? MembershipId { get; set; }
    [Classification(DataClass.Internal)] public int IdleTimeoutMinutes { get; set; } = DefaultIdleTimeoutMinutes;
    /// <summary>Coarse browser and OS ("Chrome on Windows") so people recognise their devices; never the raw User-Agent or IP.</summary>
    [Classification(DataClass.Internal)] public required string Device { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset LastSeenAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>SHA-256 of the native app's current refresh token (or of its one-time sign-in code); null for a browser session.</summary>
    [Classification(DataClass.Internal)] public byte[]? RefreshTokenHash { get; set; }
    /// <summary>SHA-256 of the token the last rotation replaced: only that one coming back is a replay; any other mismatch is a forgery (#883).</summary>
    [Classification(DataClass.Internal)] public byte[]? PreviousRefreshTokenHash { get; set; }
    /// <summary>
    /// The native app's push token for this device (ADR-011 §8). Every revoke clears it, so a revoked device gets no push; a session that
    /// only timed out keeps it, so the sender (MDV-P5-038) pushes to live sessions only. A credential to reach the phone: never logged.
    /// </summary>
    [Classification(DataClass.Personal)] public string? PushToken { get; set; }

    public const int PushTokenMaxLength = 512;

    /// <summary>Expo, FCM and APNs tokens: letters, digits and a few separators ("ExponentPushToken[…]"), never spaces or markup.</summary>
    public static bool IsPushToken(string? token) =>
        token is { Length: > 0 and <= PushTokenMaxLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.' or '[' or ']');

    /// <summary>A native app session: reachable only by bearer token, never by cookie (and the reverse).</summary>
    [Classification(DataClass.Internal)] public bool IsNative => RefreshTokenHash is not null;

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && now < CreatedAt + (IsNative ? NativeMaxLifetime : MaxLifetime)
        && now < LastSeenAt + (IsNative ? NativeIdleTimeout : TimeSpan.FromMinutes(IdleTimeoutMinutes));

    /// <summary>
    /// "Medvya app 1.2.0 on iOS": what a native app reports about itself at sign-in (MDV-P5-033 device registration), for My devices.
    /// Anything but "ios" or "android" and a plain version number is dropped, so the label never carries free text.
    /// </summary>
    public static string AppLabel(string? platform, string? appVersion)
    {
        var os = platform switch { "ios" => "iOS", "android" => "Android", _ => "unknown device" };
        var version = appVersion is { Length: > 0 and <= 20 } && appVersion.All(c => char.IsAsciiDigit(c) || c == '.') ? appVersion + " " : "";
        return $"Medvya app {version}on {os}";
    }

    /// <summary>"Chrome on Windows" style label from a User-Agent; unknown parts stay generic.</summary>
    public static string DeviceLabel(string? userAgent)
    {
        var ua = userAgent ?? "";
        // Order matters: Edge and Opera also say Chrome, Chrome also says Safari.
        var browser = ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera"
            : ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("Chrome/", StringComparison.Ordinal) || ua.Contains("CriOS/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : "Browser";
        var os = ua.Contains("Android", StringComparison.Ordinal) ? "Android"
            : ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal) ? "iOS"
            : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Mac OS X", StringComparison.Ordinal) ? "macOS"
            : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : "unknown device";
        return $"{browser} on {os}";
    }
}
