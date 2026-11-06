using Host.Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-029: the full-stack browser journey moves the API's clock; configuration must never be able to do that outside Development.</summary>
public sealed class DevClockTests
{
    const string Morning = "2026-11-03T04:00:00Z";

    static TimeProvider ClockOf(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment(environment).UseSetting(DevClock.Setting, Morning));
        return factory.Services.GetRequiredService<TimeProvider>();
    }

    [Fact]
    public void DevelopmentMayStartTheClockAtAGivenMorning() =>
        Assert.InRange(ClockOf("Development").GetUtcNow(), DateTimeOffset.Parse(Morning, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(Morning, System.Globalization.CultureInfo.InvariantCulture).AddMinutes(5));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void OtherEnvironmentsIgnoreTheSettingAndKeepTheSystemClock(string environment) =>
        Assert.Same(TimeProvider.System, ClockOf(environment));
}
