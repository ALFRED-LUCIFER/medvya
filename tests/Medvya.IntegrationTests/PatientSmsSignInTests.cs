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

    /// <summary>Holds the send until released and keeps the token it was given, so a test can see whether the caller could cancel it.</summary>
    sealed class HeldSms : ISmsGateway
    {
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(cancellationToken);
            await Release.Task;
        }
    }

    [Fact]
    public async Task ASendIsCountedBeforeItGoesOutAndAHangUpCannotCancelIt()
    {
        var held = new HeldSms();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ISmsGateway>(held)));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var hangUp = new CancellationTokenSource();
        var request = client.PostAsJsonAsync(U("/v1/patient-sign-in/code"), new { countryCode = "IN", mobile = number, language = "en" }, hangUp.Token);

        var sendToken = await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        // While the provider holds the SMS, the send is already counted and committed, so a dropped request cannot skip the limits.
        await using (var db = sql.Identity())
        {
            var row = await db.SmsSignIns.AsNoTracking().SingleAsync(r => r.PhoneHash == SmsSignInRules.HashPhone("+91" + number));
            Assert.Equal(1, row.CodesSent);
            Assert.Equal(clock.GetUtcNow(), row.LastSentAt);
            Assert.NotNull(row.CodeHash);
        }
        Assert.False(sendToken.CanBeCanceled); // the caller hanging up cannot cancel an SMS that may already be on its way

        await hangUp.CancelAsync();
        held.Release.SetResult();
        try { await request.WaitAsync(TimeSpan.FromSeconds(30)); } catch (OperationCanceledException) { } // either way the caller is gone
        Assert.Equal(("too_many_codes", 1), await Body(await RequestCode(number))); // the resend wait applies
    }

    [Fact]
    public async Task AboveTheHourlyBudgetForAllNumbersNothingIsSent()
    {
        clock.Advance(TimeSpan.FromDays(30)); // past every other test's sends
        using var app = factory.WithWebHostBuilder(b => b.UseSetting("Notifications:Sms:SignInCodesPerHour", "2"));
        Task<HttpResponseMessage> Ask(string mobile) =>
            app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") })
                .PostAsJsonAsync(U("/v1/patient-sign-in/code"), new { countryCode = "IN", mobile, language = "en" });

        Assert.Equal(HttpStatusCode.Accepted, (await Ask("8" + number[1..])).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Ask("7" + number[1..])).StatusCode);
        var over = await Ask(number);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, over.StatusCode);
        Assert.Equal(("send_failed", (int?)null), await Body(over));
        Assert.Equal(2, sms.Sent.Count);

        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(HttpStatusCode.Accepted, (await Ask(number)).StatusCode);
    }

    [Fact]
    public async Task ParallelWrongCodesCountOneByOneSoNoMoreThanFiveAreTried()
    {
        await RequestCode(number);
        var code = LastCode();
        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ => await Body(await Verify(number, Wrong(code)))));

        Assert.Equal(SmsSignInRules.MaxWrongCodes - 1, answers.Count(a => a.Error == "wrong_code"));
        Assert.Equal(10 - (SmsSignInRules.MaxWrongCodes - 1), answers.Count(a => a.Error == "locked"));
        await using var db = sql.Identity();
        var row = await db.SmsSignIns.AsNoTracking().SingleAsync(r => r.PhoneHash == SmsSignInRules.HashPhone("+91" + number));
        Assert.Equal(SmsSignInRules.MaxWrongCodes, row.WrongCodes);
        Assert.Equal(clock.GetUtcNow() + SmsSignInRules.LockPeriod, row.LockedUntil);
        Assert.Equal("locked", (await Body(await Verify(number, code))).Error);
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
