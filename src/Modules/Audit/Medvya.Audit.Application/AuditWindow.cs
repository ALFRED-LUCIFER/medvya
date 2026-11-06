using System.Collections.Concurrent;

namespace Medvya.Audit.Application;

/// <summary>
/// Remembers that a user's identical read was just audited, so a screen that refetches every few seconds leaves one row per
/// window instead of one per call. Only Allowed reads may be skipped, and only after the window's first row was written
/// successfully (fail closed); Denied, NotFound and Rejected answers are always written by the caller.
/// </summary>
// shortcut: per-instance memory, so a restart or a second API instance writes one extra row per window; move the window into
// the database (or a shared cache) before running more than one instance (MDV-P0-007).
public sealed class AuditWindow(TimeProvider clock)
{
    /// <summary>
    /// Window for reads a screen repeats on its own: the board's refetch on every live update and its watch, the schedule's
    /// refresh and the visit panel's message poll (MDV-P1-052). The first read in the window is always written.
    /// </summary>
    public static readonly TimeSpan Repeated = TimeSpan.FromMinutes(5);

    readonly ConcurrentDictionary<string, DateTimeOffset> until = new();

    /// <summary>The key names the tenant, user, action and exactly what was read, so nothing else is ever hidden by it.</summary>
    public static string Key(Guid tenantId, Guid userId, string action, string what) => $"{tenantId:N}|{userId:N}|{action}|{what}";

    public bool Covered(string key) => until.TryGetValue(key, out var end) && end > clock.GetUtcNow();

    public void Record(string key, TimeSpan window)
    {
        var now = clock.GetUtcNow();
        foreach (var (k, end) in until) if (end <= now) until.TryRemove(k, out _); // keeps the map to the live windows
        until[key] = now + window;
    }
}
