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

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && now < CreatedAt + MaxLifetime && now < LastSeenAt + TimeSpan.FromMinutes(IdleTimeoutMinutes);

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
