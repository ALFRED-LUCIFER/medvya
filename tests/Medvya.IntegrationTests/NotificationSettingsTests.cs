using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Medvya.IntegrationTests;

/// <summary>#912: a configured channel list replaces the defaults (dev turns SMS off with Notifications__Channels__0=Email).</summary>
public sealed class NotificationSettingsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    NotificationOptions Options(params (string Key, string Value)[] settings)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Medvya", sql.ConnectionString);
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });
        return factory.Services.GetRequiredService<IOptions<NotificationOptions>>().Value;
    }

    [Fact]
    public void ConfiguredChannelListReplacesTheDefaults()
    {
        var o = Options(("Notifications:Channels:0", "Email"), ("Notifications:BackoffMinutes:0", "2"));
        Assert.Equal([NotificationChannel.Email], o.Channels);
        Assert.Equal([2], o.BackoffMinutes);
    }

    [Fact]
    public void WithoutSettingsTheDefaultsStay()
    {
        var o = Options();
        Assert.Equal([NotificationChannel.Email, NotificationChannel.Sms], o.Channels);
        Assert.Equal([1, 5, 30], o.BackoffMinutes);
    }
}
