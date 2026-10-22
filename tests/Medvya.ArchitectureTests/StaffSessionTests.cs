using Medvya.Identity.Domain;

namespace Medvya.ArchitectureTests;

/// <summary>MDV-P0-012: when a server-side session stops being valid.</summary>
public class StaffSessionTests
{
    static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    static StaffSession Session(int idleMinutes = 15) => new() { Device = "Chrome on Windows", CreatedAt = Start, LastSeenAt = Start, IdleTimeoutMinutes = idleMinutes };

    [Fact]
    public void IdleTimeoutEndsTheSession()
    {
        var s = Session();
        Assert.True(s.IsActive(Start.AddMinutes(14)));
        Assert.False(s.IsActive(Start.AddMinutes(15)));
        Assert.True(Session(idleMinutes: 30).IsActive(Start.AddMinutes(20)));
    }

    [Fact]
    public void ActivityNeverOutlivesTheEightHourCap()
    {
        var s = Session();
        s.LastSeenAt = Start.AddHours(8).AddMinutes(-1);
        Assert.True(s.IsActive(Start.AddHours(8).AddMinutes(-1)));
        Assert.False(s.IsActive(Start.AddHours(8)));
    }

    [Fact]
    public void RevokedSessionsAreOverAtOnce()
    {
        var s = Session();
        s.RevokedAt = Start.AddMinutes(1);
        Assert.False(s.IsActive(Start.AddMinutes(1)));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 Edg/141.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15", "Safari on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox on Linux")]
    [InlineData(null, "Browser on unknown device")]
    public void DeviceLabelIsCoarse(string? userAgent, string expected) => Assert.Equal(expected, StaffSession.DeviceLabel(userAgent));
}
