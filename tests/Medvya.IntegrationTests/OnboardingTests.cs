using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Host.Api.Auth;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-004: sign up, fill the wizard, leave and resume, finish. Real SQL Server, real OIDC code + PKCE against the in-process
/// development provider. Synthetic people only.
/// </summary>
public sealed partial class OnboardingTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    readonly Outbox outbox = new();
    readonly List<WebApplicationFactory<Program>> factories = [];

    public void Dispose() { foreach (var f in factories) f.Dispose(); }

    readonly LogCapture logs = new();

    sealed class LogCapture : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{logLevel}: {formatter(state, exception)}");
    }

    sealed class Outbox : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) { lock (Sent) Sent.Add(message); return Task.CompletedTask; }
    }

    HttpClient Browser(string? deploymentCountry = null)
    {
        WebApplicationFactory<Program> f = null!;
        f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
                .UseSetting("Authentication:Oidc:Authority", "http://localhost/dev-idp")
                .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test");
            if (deploymentCountry is not null) b.UseSetting("Onboarding:DeploymentCountry", deploymentCountry);
            b.ConfigureTestServices(s =>
            {
                s.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = f.Server.CreateHandler());
                s.AddSingleton<IEmailSender>(outbox);
                s.AddLogging(l => l.AddProvider(logs));
            });
        });
        factories.Add(f);
        return Client(f);
    }

    static HttpClient Client(WebApplicationFactory<Program> f)
    {
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(StaffAuthentication.CsrfHeader, "1");
        return client;
    }

    /// <summary>Signs in through the provider; "new" is a person who is not a Medvya user yet. With <paramref name="signUp"/> the account is created.</summary>
    static async Task SignIn(HttpClient browser, string loginHint, bool signUp)
    {
        var challenge = await browser.GetAsync(new Uri($"/bff/login?returnUrl=%2Fonboarding{(signUp ? "&signup=true" : "")}", UriKind.Relative));
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint={loginHint}"));
        var callback = await browser.GetAsync(authorize.Headers.Location);
        Assert.Equal("/onboarding", callback.Headers.Location?.OriginalString);
    }

    static Uri Api(string path = "") => new($"/v1/onboarding{path}", UriKind.Relative);

    static object Data(string number = "84213", string authority = "TNMC", string gstin = "", object? team = null) => new
    {
        profile = new { fullName = "Arjun Rao (synthetic)", mode = "Solo", specialties = new[] { "general-practice", "diabetes" }, qualifications = "MBBS", languages = "English, Tamil" },
        licence = new { authority, number, certificateName = "council-certificate.pdf" },
        clinic = new { name = "Rao Clinic (synthetic)", address = "12 Test Street, Chennai", registrationNumber = "CE-0001", taxId = gstin },
        hours = new
        {
            weekdays = new[] { new { start = "09:00", end = "13:00" }, new { start = "17:00", end = "20:00" } },
            saturday = new[] { new { start = "09:00", end = "14:00" } }, slotMinutes = 20, newVisitFee = 100_000, followUpFee = 60_000,
            days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
        },
        templates = new[] { "general-opd", "diabetes-review" },
        team = team ?? Array.Empty<object>(),
        patients = "Move",
    };

    static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task SignUpFillLeaveResumeAndFinishCreatesThePractice()
    {
        using var browser = Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(Api())).StatusCode);

        await SignIn(browser, "new", signUp: true);
        var me = await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)));
        Assert.Empty(me.GetProperty("memberships").EnumerateArray()); // a new account reaches no clinic
        var userId = me.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync(Api())).StatusCode);

        var started = await Json(await browser.PostAsJsonAsync(Api(), new { country = "IN" }));
        Assert.Equal("IN", started.GetProperty("country").GetString());
        Assert.Contains(started.GetProperty("rules").GetProperty("authorities").EnumerateArray(), a => a.GetProperty("code").GetString() == "TNMC");

        // A failed check saves nothing and names the field, not the value.
        var bad = await browser.PutAsJsonAsync(Api(), new { step = 3, validate = 1, data = Data(number: "1") });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await Json(bad)).GetProperty("errors");
        Assert.Equal("format", errors.GetProperty("licence.number")[0].GetString());
        Assert.Equal("{}", (await Json(await browser.GetAsync(Api()))).GetProperty("data").GetRawText());

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync(Api(), new { step = 3, validate = 1, data = Data() })).StatusCode);

        // Leave, sign in again as the same person: same step, same values.
        await using (var identity = sql.Identity())
        {
            var subject = (await identity.Users.SingleAsync(u => u.Id == userId)).Subject;
            using var again = Browser();
            await SignIn(again, subject, signUp: false);
            var resumed = await Json(await again.GetAsync(Api()));
            Assert.Equal(3, resumed.GetProperty("step").GetInt32());
            Assert.Equal("84213", resumed.GetProperty("data").GetProperty("licence").GetProperty("number").GetString());
        }

        // The team step sends an invitation through the P0 flow; finishing creates everything.
        var team = new[] { new { email = "joseph@example.test", role = "Receptionist" } };
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync(Api(), new { step = 5, validate = 3, data = Data(team: team) })).StatusCode);
        var finished = await browser.PostAsync(Api("/finish"), null);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        var result = await Json(finished);
        Assert.Equal("SoloDoctor", result.GetProperty("role").GetString());
        Assert.Equal(1, result.GetProperty("invitationsSent").GetInt32());
        Assert.Contains(outbox.Sent, m => m.To == "joseph@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await browser.GetAsync(Api())).StatusCode); // the draft is done

        var context = await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)));
        Assert.Equal("Arjun Rao (synthetic)", context.GetProperty("user").GetProperty("displayName").GetString());
        var membership = Assert.Single(context.GetProperty("memberships").EnumerateArray());
        var tenantId = membership.GetProperty("tenantId").GetGuid();
        Assert.Equal(result.GetProperty("membershipId").GetGuid(), membership.GetProperty("id").GetGuid());

        await using (var db = sql.Context(tenantId))
        {
            var tenant = await db.Tenants.SingleAsync();
            Assert.Equal(("IN", "Rao Clinic (synthetic)"), (tenant.CountryCode, tenant.Name));
            await AssertCountryAudited(tenantId, userId, "IN");
            Assert.Equal("Asia/Kolkata", (await db.Facilities.SingleAsync()).TimeZone);
            var owner = await db.Memberships.SingleAsync(m => m.UserId == userId);
            Assert.Equal((Role.SoloDoctor, MembershipStatus.Active), (owner.Role, owner.Status));
            var profile = await db.PracticeDefaultsSet.SingleAsync();
            Assert.Equal((20, 100_000L, 60_000L, "INR"), (profile.SlotMinutes, profile.NewVisitFeeMinor, profile.FollowUpFeeMinor, profile.Currency));
            var address = await db.Profiles.SingleAsync();
            Assert.Equal(("12 Test Street, Chennai", "CE-0001"), (address.Address, address.RegistrationNumber));
            var open = await db.OperatingHours.Where(h => h.Weekday == DayOfWeek.Monday).SingleAsync();
            Assert.Equal((new TimeOnly(9, 0), new TimeOnly(20, 0)), (open.Opens, open.Closes)); // first start to last end
            var saturday = await db.OperatingHours.SingleAsync(h => h.Weekday == DayOfWeek.Saturday);
            Assert.Equal((new TimeOnly(9, 0), new TimeOnly(14, 0)), (saturday.Opens, saturday.Closes)); // Saturday keeps its own hours
            Assert.Equal(6, await db.OperatingHours.CountAsync()); // the six days switched on; Sunday stays closed
            Assert.Equal(("General practice, Diabetes", "Move"), (profile.Specialty, profile.PatientStart));
            Assert.Contains("17:00", profile.WeekdayHours, StringComparison.Ordinal);
            var licence = await db.DoctorLicences.SingleAsync();
            Assert.Null(licence.VerifiedAt);
            Assert.Equal(1, await db.Invitations.CountAsync());
        }
        await using (var other = sql.Context(Guid.NewGuid()))
        {
            Assert.Empty(await other.PracticeDefaultsSet.IgnoreQueryFilters().ToListAsync()); // row-level security: another tenant sees none
            Assert.Empty(await other.DoctorLicences.IgnoreQueryFilters().ToListAsync());
        }

        // The owner doctor is bookable straight away: step 2's windows are their sessions on the open days at the chosen slot length, and they show in the day view.
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsJsonAsync(new Uri("/v1/me/facility", UriKind.Relative), new { membershipId = membership.GetProperty("id").GetGuid() })).StatusCode);
        var facilityId = (await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("active").GetProperty("facilityId").GetGuid();
        var templates = (await Json(await browser.GetAsync(new Uri($"/v1/schedule/templates?facilityId={facilityId}", UriKind.Relative)))).EnumerateArray().ToList();
        var mine = Assert.Single(templates, t => t.GetProperty("calendarId").GetGuid() == userId);
        Assert.Equal(20, mine.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(11, mine.GetProperty("hours").GetArrayLength()); // two windows on each of five weekdays, one on Saturday
        var day = await Json(await browser.GetAsync(new Uri($"/v1/schedule?facilityId={facilityId}&date=2026-11-02", UriKind.Relative))); // a Monday
        Assert.Equal(userId, Assert.Single(day.GetProperty("columns").EnumerateArray()).GetProperty("calendarId").GetGuid());

        // Until the licence is verified: booking stays, recording and signing are off (the server removes them from the session).
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsJsonAsync(new Uri("/v1/me/facility", UriKind.Relative), new { membershipId = membership.GetProperty("id").GetGuid() })).StatusCode);
        var pending = (await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("active");
        Assert.True(pending.GetProperty("licencePending").GetBoolean());
        Assert.Equal("Full", pending.GetProperty("capabilities").GetProperty("RegisterBookPatients").GetString());
        Assert.False(pending.GetProperty("capabilities").TryGetProperty("ClinicalNotes", out _));
        Assert.False(pending.GetProperty("capabilities").TryGetProperty("RecordingConsentCapture", out _));

        await using (var db = sql.Context(tenantId))
            await db.DoctorLicences.ExecuteUpdateAsync(s => s.SetProperty(l => l.VerifiedAt, DateTimeOffset.UtcNow));
        var verified = (await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("active");
        Assert.False(verified.GetProperty("licencePending").GetBoolean());
        Assert.Equal("Sign", verified.GetProperty("capabilities").GetProperty("ClinicalNotes").GetString());

        // The front desk member invited in the team step joins with the front-desk role and the server keeps them out of clinical notes
        // and consultations (MDV-P1-061). Mutation check: invite with another role in /finish and this fails.
        var token = LinkToken().Match(outbox.Sent.Single(m => m.To == "joseph@example.test").Body).Groups[1].Value;
        using var desk = Browser();
        var challenge = await desk.GetAsync(new Uri($"/bff/login?returnUrl=%2F&invitation={token}", UriKind.Relative));
        var authorize = await desk.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint=new"));
        Assert.Equal("/", (await desk.GetAsync(authorize.Headers.Location)).Headers.Location?.OriginalString);
        var deskMembership = Assert.Single((await Json(await desk.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("memberships").EnumerateArray());
        Assert.Equal("Receptionist", deskMembership.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await desk.PostAsJsonAsync(new Uri("/v1/me/facility", UriKind.Relative), new { membershipId = deskMembership.GetProperty("id").GetGuid() })).StatusCode);
        var deskAccess = (await Json(await desk.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("active").GetProperty("capabilities");
        Assert.False(deskAccess.TryGetProperty("ClinicalNotes", out _));
        Assert.False(deskAccess.TryGetProperty("Patient360Chart", out _));
        var visit = new Uri($"/v1/encounters/{Guid.NewGuid()}", UriKind.Relative);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.GetAsync(visit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(visit)).StatusCode); // the doctor gets past the policy: the refusal is the role's
    }

    [GeneratedRegex(@"https://clinic\.test/invite/([A-Za-z0-9_-]+)")]
    private static partial Regex LinkToken();

    /// <summary>Mutation check: remove the catch in /finish and this fails (500); remove the log call and the log assertion fails.</summary>
    [Fact]
    public async Task HoursTheScheduleRejectsStillFinishOnboardingAndLeaveAWarning()
    {
        using var browser = Browser();
        await SignIn(browser, "new", signUp: true);
        await browser.PostAsJsonAsync(Api(), new { country = "IN" });
        // The wizard accepts 09:07; the schedule rules need the 5-minute grid.
        var data = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Data()))!;
        data["hours"]!["weekdays"] = System.Text.Json.Nodes.JsonNode.Parse("""[{"start":"09:07","end":"13:00"}]""");
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync(Api(), new { step = 5, validate = 3, data })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync(Api("/finish"), null)).StatusCode);

        var tenantId = (await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("memberships")[0].GetProperty("tenantId").GetGuid();
        await using var scheduling = sql.Scheduling(tenantId);
        Assert.Empty(await scheduling.Templates.ToListAsync());
        Assert.Contains(logs.Messages, m => m.StartsWith("Warning: Onboarding finished without the owner's bookable sessions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOwnerWhoIsNotADoctorBecomesPracticeAdminWithoutALicence()
    {
        using var browser = Browser();
        await SignIn(browser, "new", signUp: true);
        await browser.PostAsJsonAsync(Api(), new { country = "AE" });
        var data = new
        {
            profile = new { fullName = "K. Lakshmi (synthetic)", mode = "Clinic" },
            clinic = new { name = "Al Noor Clinic (synthetic)", address = "1 Test Road, Abu Dhabi", registrationNumber = "FL-1", taxId = "100123456700003" },
            hours = new { weekdays = new[] { new { start = "08:00", end = "16:00" } }, saturday = Array.Empty<object>(), slotMinutes = 15, newVisitFee = 35_000, followUpFee = 25_000 },
            templates = Array.Empty<string>(), team = Array.Empty<object>(),
        };
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync(Api(), new { step = 5, data })).StatusCode);
        var finished = await Json(await browser.PostAsync(Api("/finish"), null));
        Assert.Equal("PracticeAdmin", finished.GetProperty("role").GetString());

        var tenantId = (await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("memberships")[0].GetProperty("tenantId").GetGuid();
        await using var db = sql.Context(tenantId);
        Assert.Equal("AE", (await db.Tenants.SingleAsync()).CountryCode);
        await AssertCountryAudited(tenantId, (await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("user").GetProperty("id").GetGuid(), "AE");
        Assert.Equal("AED", (await db.PracticeDefaultsSet.SingleAsync()).Currency);
        Assert.Empty(await db.DoctorLicences.ToListAsync());
        await using var scheduling = sql.Scheduling(tenantId);
        Assert.Empty(await scheduling.Templates.ToListAsync()); // not a doctor: no sessions
    }

    /// <summary>MDV-P1-002: the country choice is in the new tenant's audit trail, once, by the owner. Mutation check: drop the audit write
    /// in PracticeProvisioning, or hard-code the country in its action, and the IN or AE finish test fails.</summary>
    async Task AssertCountryAudited(Guid tenantId, Guid owner, string country)
    {
        await using var audit = sql.Audit(tenantId);
        var row = Assert.Single(await audit.Events.Where(e => e.Action.StartsWith("onboarding.complete")).ToListAsync());
        Assert.Equal(($"onboarding.complete.{country}", "tenant", tenantId, owner, AuditOutcome.Allowed), (row.Action, row.ResourceType, row.ResourceId, row.ActorUserId, row.Outcome));
    }

    // MDV-P1-002: one deployment serves one country, so outside Development an unset or unknown country stops the host instead of
    // accepting every country. Mutation check: remove the startup check in Program.cs and the refusing rows start.
    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Staging", "XX", false)]
    [InlineData("Development", "XX", false)]
    [InlineData("Production", "IN", true)]
    [InlineData("Development", null, true)]
    public void TheDeploymentCountryFailsClosedOutsideDevelopment(string environment, string? country, bool starts)
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            if (country is not null) b.UseSetting("Onboarding:DeploymentCountry", country);
        });
        if (starts) host.CreateClient().Dispose();
        else Assert.Contains("Onboarding:DeploymentCountry", Assert.Throws<InvalidOperationException>(() => host.CreateClient()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinishingAnIncompleteDraftCreatesNothing()
    {
        using var browser = Browser();
        await SignIn(browser, "new", signUp: true);
        await browser.PostAsJsonAsync(Api(), new { country = "IN" });
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsJsonAsync(Api(), new { step = 2, data = new { profile = new { fullName = "Arjun Rao (synthetic)", mode = "Solo", specialty = "ENT" } } })).StatusCode);
        var finished = await browser.PostAsync(Api("/finish"), null);
        Assert.Equal(HttpStatusCode.BadRequest, finished.StatusCode);
        Assert.Empty((await Json(await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative)))).GetProperty("memberships").EnumerateArray());
    }

    [Fact]
    public async Task ADeploymentOnlyStartsPracticesInItsOwnCountry()
    {
        using var browser = Browser(deploymentCountry: "AE");
        await SignIn(browser, "new", signUp: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync(Api(), new { country = "IN" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsJsonAsync(Api(), new { country = "AE" })).StatusCode);
    }

    [Fact]
    public async Task SigningInWithoutSignUpStillRefusesAnUnknownAccount()
    {
        using var browser = Browser();
        var challenge = await browser.GetAsync(new Uri("/bff/login?returnUrl=%2Fonboarding", UriKind.Relative));
        var authorize = await browser.GetAsync(new Uri($"{challenge.Headers.Location}&login_hint=new"));
        var callback = await browser.GetAsync(authorize.Headers.Location);
        Assert.Equal("/?signin=failed", callback.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// The server-side gate behind every capability policy (CapabilityHandler → IStaffAccess): a doctor with an unverified licence
    /// can register and book but not record or sign. Mutation check: remove LicenceRules.Apply from StaffAccess and this fails.
    /// </summary>
    [Fact]
    public async Task AccessToRecordingAndSigningIsNoneUntilTheLicenceIsVerified()
    {
        var (tenantId, facilityId, userId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using (var db = sql.Context(tenantId))
        {
            var membershipId = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Licence Gate Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            db.Facilities.Add(new Facility { Id = facilityId, TenantId = tenantId, Name = "Main (synthetic)", TimeZone = "Asia/Kolkata" });
            db.Memberships.Add(new Membership { Id = membershipId, TenantId = tenantId, UserId = userId, FacilityId = facilityId, Role = Role.SoloDoctor, Status = MembershipStatus.Active });
            db.DoctorLicences.Add(new DoctorLicence { MembershipId = membershipId, TenantId = tenantId, Authority = "TNMC", Number = "84213", SubmittedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        async Task<Access> AccessTo(Capability capability)
        {
            await using var db = sql.Context(tenantId);
            return await new StaffAccess(db, new FacilityDirectory(db), new Who(userId), new At(facilityId)).AccessAsync(capability);
        }

        Assert.Equal(Medvya.Tenancy.Domain.Access.Full, await AccessTo(Capability.RegisterBookPatients));
        Assert.Equal(Medvya.Tenancy.Domain.Access.Full, await AccessTo(Capability.CheckInQueue));
        foreach (var gated in new[] { Capability.ClinicalNotes, Capability.PrescriptionsOrders, Capability.RecordingConsentCapture, Capability.AiDraftsEvidence, Capability.ResultReviewAcknowledge })
            Assert.Equal(Medvya.Tenancy.Domain.Access.None, await AccessTo(gated));

        await using (var db = sql.Context(tenantId))
            await db.DoctorLicences.ExecuteUpdateAsync(s => s.SetProperty(l => l.VerifiedAt, DateTimeOffset.UtcNow));
        Assert.Equal(Medvya.Tenancy.Domain.Access.Sign, await AccessTo(Capability.ClinicalNotes));
        Assert.Equal(Medvya.Tenancy.Domain.Access.Full, await AccessTo(Capability.RecordingConsentCapture));
    }

    sealed class Who(Guid id) : ICurrentUser { public Guid? UserId => id; }
    sealed class At(Guid id) : ICurrentFacility { public Guid? FacilityId => id; }
}
