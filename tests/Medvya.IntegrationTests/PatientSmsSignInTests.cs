using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medvya.Identity.Api;
using Medvya.Identity.Domain;
using Medvya.Notifications.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P5-033 check 3: patient SMS sign-in. Five wrong codes lock the number for a period, and the answer says so in minutes.</summary>
public sealed partial class PatientSmsSignInTests : IClassFixture<PostgresFixture>, IDisposable
{
    readonly PostgresFixture sql;
    readonly ManualClock clock = new(SchedulingHarness.MondayMorning);
    readonly FakeSms sms = new();
    readonly WebApplicationFactory<Program> factory;
    // A fresh number per test (the table is shared by the class), in the synthetic cast's 90000 range.
    readonly string number = "90000" + Random.Shared.Next(10_000, 100_000);

    sealed class FakeSms : ISmsGateway
    {
        public List<SmsMessage> Sent { get; } = [];
        public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    public PatientSmsSignInTests(PostgresFixture sql)
    {
        this.sql = sql;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:SignInPerMinute", "1000")
            .UseSetting("Notifications:Sms:DefaultSender", "MEDVYA")
            .UseSetting("Notifications:Sms:DltTemplateIds:SignInCode.en", "1107000000000000009")
            .ConfigureTestServices(s =>
            {
                s.AddSingleton<TimeProvider>(clock);
                s.AddSingleton<ISmsGateway>(sms);
            }));
    }

    public void Dispose() => factory.Dispose();

    HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    static Uri U(string path) => new(path, UriKind.Relative);

    Task<HttpResponseMessage> RequestCode(string mobile) =>
        Client().PostAsJsonAsync(U("/v1/patient-sign-in/code"), new { countryCode = "IN", mobile, language = "en" });

    Task<HttpResponseMessage> Verify(string mobile, string code) =>
        Client().PostAsJsonAsync(U("/v1/patient-sign-in/verify"), new { countryCode = "IN", mobile, code });

    string LastCode() => CodeIn().Match(sms.Sent[^1].Text).Value;

    static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    static async Task<(string? Error, int? Minutes)> Body(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null,
            json.RootElement.TryGetProperty("retryAfterMinutes", out var m) ? m.GetInt32() : null);
    }

    [GeneratedRegex(@"\d{6}")] private static partial Regex CodeIn();

    [Fact]
    public async Task ARightCodeIsVerifiedOnceAndTheSmsSaysNothingAboutCare()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCode(number)).StatusCode);
        var sent = Assert.Single(sms.Sent);
        Assert.Equal("+91" + number, sent.To);
        Assert.Equal($"{LastCode()} is your Medvya sign-in code. It expires in 10 minutes. Never share it with anyone.", sent.Text);
        Assert.Equal("1107000000000000009", sent.TemplateId);

        Assert.Equal(HttpStatusCode.OK, (await Verify(number, LastCode())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(number, LastCode())).StatusCode); // a code works once
    }

    [Fact]
    public async Task FiveWrongCodesLockTheNumberAndEvenTheRightCodeIsRefusedUntilThePeriodEnds()
    {
        await RequestCode(number);
        var code = LastCode();
        for (var i = 1; i <= 4; i++)
        {
            var wrong = await Verify(number, Wrong(code));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal(("wrong_code", (int?)null), await Body(wrong));
        }
        var fifth = await Verify(number, Wrong(code));
        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        Assert.Equal(("locked", 15), await Body(fifth));

        // The sixth try, with the right code, is still refused while the number is locked; so is a new code.
        clock.Advance(TimeSpan.FromMinutes(10));
        var sixth = await Verify(number, code);
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Equal(("locked", 5), await Body(sixth));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RequestCode(number)).StatusCode);
        Assert.Single(sms.Sent);

        // After the period: the old code was ended by the lock; a new one works, and the count starts again.
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(number, code)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCode(number)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Verify(number, LastCode())).StatusCode);
    }

    [Fact]
    public async Task ANumberNobodyAskedACodeForAnswersTheSameAndLocksTheSame()
    {
        await RequestCode(number);
        var known = await Verify(number, Wrong(LastCode()));
        var unknown = "8" + number[1..];
        var stranger = await Verify(unknown, "123456");
        Assert.Equal(known.StatusCode, stranger.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await stranger.Content.ReadAsStringAsync());

        for (var i = 2; i <= 4; i++) await Verify(unknown, "123456");
        Assert.Equal(("locked", 15), await Body(await Verify(unknown, "123456")));
    }

    [Fact]
    public async Task CodesAreLimitedPerNumber()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCode(number)).StatusCode);
        var again = await RequestCode(number);
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.Equal(("too_many_codes", 1), await Body(again));
        for (var i = 2; i <= 5; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(HttpStatusCode.Accepted, (await RequestCode(number)).StatusCode);
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(("too_many_codes", 55), await Body(await RequestCode(number)));
        clock.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(HttpStatusCode.Accepted, (await RequestCode(number)).StatusCode);
    }

    [Fact]
    public async Task OnlyHashesAreStoredAndTheNumberNeverReachesAUrl()
    {
        await RequestCode(number);
        var code = LastCode();
        await using var db = sql.Identity();
        var row = await db.SmsSignIns.AsNoTracking().SingleAsync(r => r.PhoneHash == SmsSignInRules.HashPhone("+91" + number));
        Assert.Equal(32, row.CodeHash!.Length);
        Assert.Equal(SmsSignInRules.HashCode(row.PhoneHash, code), row.CodeHash);
        var stored = Encoding.ASCII.GetString(row.PhoneHash) + Encoding.ASCII.GetString(row.CodeHash);
        Assert.DoesNotContain(number, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(code, stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidNumberIsRefusedBeforeAnythingIsSentOrStored()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await RequestCode("12345")).StatusCode);
        Assert.Empty(sms.Sent);
    }

    [Fact]
    public void TheEndpointsArePublicAndLimitedPerClient()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/v1/patient-sign-in", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, endpoints.Count);
        Assert.All(endpoints, e =>
        {
            Assert.NotNull(e.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Equal(InvitationEndpoints.SignInRateLimit, e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        });
    }
}
