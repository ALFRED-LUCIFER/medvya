using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Domain;
using Medvya.Scheduling.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P1-007 on real SQL Server and through the real endpoints: the Add doctor wizard's server side (licence rules per country,
/// invitation, private signature image), the back-office licence decision that only Medvya operations may make, and the gate that keeps
/// an unverified doctor from recording or signing.
/// </summary>
public sealed class DoctorTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly byte[] Png = TestImages.Png;
    const string Email = "sana.khan@example.test", Contact = "desk@clinic.example";

    readonly SchedulingHarness h = new(sql);
    readonly Guid ops = Guid.NewGuid();
    readonly Outbox mail = new();
    WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        await h.SeedAsync();
        await h.Settings(h.Admin).SaveProfileAsync(new SaveProfile { Name = "Iyer Family Clinic (synthetic)", Email = Contact });
    }

    public async Task DisposeAsync()
    {
        await h.CloseAsync();
        if (factory is not null) await factory.DisposeAsync();
    }

    sealed class Outbox : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        /// <summary>Simulates the mail service refusing the message.</summary>
        public bool Down { get; set; }
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Down) throw new InvalidOperationException("mail server down");
            lock (Sent) Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>The user in X-Test-User, in the test tenant and facility (as the facility picker sets them).</summary>
    sealed class SignedIn(Guid tenant, Guid facility) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                if (ctx.Request.Headers["X-Test-User"].ToString() is { Length: > 0 } user)
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, user), new Claim("tenant_id", tenant.ToString()), new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
        };
    }

    HttpClient Client(Role? role, Guid? user = null)
    {
        factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("Invitations:LinkBaseUrl", "https://clinic.test")
            .UseSetting("Operations:LicenceReviewers:0", ops.ToString())
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(h.Tenant, h.Facility)))
            .ConfigureTestServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<ILogoStore>(h.Logos));
                s.Replace(ServiceDescriptor.Singleton<IEmailSender>(mail));
            }));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        if (user is not null || role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", (user ?? h.ByRole[role!.Value]).ToString());
        }
        return client;
    }

    object Body(string? number = "84213", string authority = "TNMC", string? expiresOn = "2031-12-31", string? category = "Permanent", string email = Email) => new
    {
        fullName = "Dr. Sana Khan (synthetic)", email, specialty = "Paediatrics", qualifications = "MBBS, MD", languages = "English, Hindi", facilityId = h.Facility,
        authority, licenceNumber = number, category, expiresOn, newVisitFee = 50000, followUpFee = 30000,
    };

    async Task<Guid> AddDoctor(object? body = null)
    {
        using var admin = Client(Role.PracticeAdmin);
        var response = await admin.PostAsJsonAsync("/v1/settings/doctors", body ?? Body());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    static Task<HttpResponseMessage> Decide(HttpClient client, Guid tenant, Guid doctor, bool verified, string? reason = null) =>
        client.PostAsJsonAsync($"/v1/operations/licences/{tenant}/{doctor}/decision", new { verified, reason });

    // ---- Add doctor ----

    [Fact]
    public async Task AddingADoctorCreatesMembershipProfileLicenceAndInvitationTogether()
    {
        var id = await AddDoctor();

        await using var db = sql.Context(h.Tenant);
        var membership = await db.Memberships.SingleAsync(m => m.Id == id);
        // Hospital doctor, never the solo-doctor role: that one carries practice configuration rights.
        Assert.Equal((Role.HospitalDoctor, MembershipStatus.Invited, h.Facility), (membership.Role, membership.Status, membership.FacilityId));
        var licence = await db.DoctorLicences.SingleAsync(l => l.MembershipId == id);
        Assert.Equal((LicenceStatus.Pending, "TNMC", "Permanent", new DateOnly(2031, 12, 31)), (LicenceRules.StatusOf(licence, new DateOnly(2026, 10, 19)), licence.Authority, licence.Category, licence.ExpiresOn));
        var profile = await db.DoctorProfiles.SingleAsync(p => p.MembershipId == id);
        Assert.Equal((50000L, 30000L, null), (profile.NewVisitFeeMinor, profile.FollowUpFeeMinor, profile.SignatureKey));
        Assert.Single(await db.Invitations.Where(i => i.MembershipId == id).ToListAsync());
        Assert.Contains("/invite/", Assert.Single(mail.Sent, m => m.To == Email).Body);
        await using var audit = sql.Audit(h.Tenant);
        Assert.Single(await audit.Events.Where(e => e.Action == "invitation.create").ToListAsync());
    }

    /// <summary>Mutation check: audit before the email again, or let the failure through as a bare 500, and this fails.</summary>
    [Fact]
    public async Task WhenTheInvitationEmailCannotBeSentNothingIsSavedOrAuditedAndTheAnswerSaysSo()
    {
        mail.Down = true;
        try
        {
            using var admin = Client(Role.PracticeAdmin);
            var response = await admin.PostAsJsonAsync("/v1/settings/doctors", Body(email: "nobody@example.test"));
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("email_failed", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        finally { mail.Down = false; }

        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.Invitations.Where(i => i.Email == "nobody@example.test").ToListAsync());
        Assert.Empty(await db.Memberships.Where(m => m.Role == Role.HospitalDoctor && m.Status == MembershipStatus.Invited).ToListAsync());
        await using var audit = sql.Audit(h.Tenant);
        Assert.Empty(await audit.Events.Where(e => e.Action == "invitation.create").ToListAsync()); // no row claiming something was created
    }

    [Theory]
    [InlineData("licence.number", "format", "!!", "TNMC", "2031-12-31", "Permanent")]
    [InlineData("licence.number", "required", "", "TNMC", "2031-12-31", "Permanent")]
    [InlineData("licence.authority", "unknown", "84213", "DHA", "2031-12-31", "Permanent")] // a UAE authority in an Indian practice
    [InlineData("licence.expiresOn", "required", "84213", "TNMC", null, "Permanent")]
    [InlineData("licence.expiresOn", "past", "84213", "TNMC", "2020-01-01", "Permanent")]
    [InlineData("licence.category", "required", "84213", "TNMC", "2031-12-31", "")]
    public async Task ALicenceWithABadNumberAuthorityCategoryOrExpiryIsRefusedAndNothingIsCreated(string field, string code, string number, string authority, string? expiresOn, string category)
    {
        using var admin = Client(Role.PracticeAdmin);
        var response = await admin.PostAsJsonAsync("/v1/settings/doctors", Body(number, authority, expiresOn, category));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(code, errors.GetProperty(field).EnumerateArray().Select(e => e.GetString()));
        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.DoctorLicences.ToListAsync());
        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task OnlyPracticeConfigurationRolesCanAddOrListDoctors()
    {
        foreach (var role in new[] { Role.Receptionist, Role.Nurse, Role.HospitalDoctor, Role.CareCoordinator, Role.Patient })
        {
            using var client = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/v1/settings/doctors", Body())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/v1/settings/doctors")).StatusCode);
        }
        await using var db = sql.Context(h.Tenant);
        Assert.Empty(await db.DoctorProfiles.ToListAsync());
    }

    // ---- Verification: Medvya operations only ----

    [Fact]
    public async Task APracticeAdminCannotVerifyALicenceAndOnlyOperationsCan()
    {
        var id = await AddDoctor();
        foreach (var role in new[] { Role.PracticeAdmin, Role.SoloDoctor, Role.HospitalIt })
        {
            using var practice = Client(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await Decide(practice, h.Tenant, id, true)).StatusCode);
        }
        using (var stranger = Client(Role.PracticeAdmin, Guid.NewGuid()))
            Assert.Equal(HttpStatusCode.Forbidden, (await Decide(stranger, h.Tenant, id, true)).StatusCode);
        await using (var db = sql.Context(h.Tenant))
            Assert.Null((await db.DoctorLicences.SingleAsync()).VerifiedAt);

        using var operations = Client(null, ops);
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, true)).StatusCode);
        await using var after = sql.Context(h.Tenant);
        Assert.NotNull((await after.DoctorLicences.SingleAsync()).VerifiedAt);
    }

    [Fact]
    public async Task VerifyingRecordsWhoAndWhenAndIsAudited()
    {
        var id = await AddDoctor();
        var before = DateTimeOffset.UtcNow;
        using (var operations = Client(null, ops))
            Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, true)).StatusCode);

        await using var db = sql.Context(h.Tenant);
        var licence = await db.DoctorLicences.SingleAsync();
        Assert.Equal(ops, licence.VerifiedBy);
        Assert.InRange(licence.VerifiedAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var change = await db.SettingsChanges.SingleAsync(c => c.Section == "licence");
        Assert.Contains("Verified", change.After);
        await using var audit = sql.Audit(h.Tenant);
        var row = await audit.Events.SingleAsync(e => e.Action == "licence.verify");
        Assert.Equal((ops, id), (row.ActorUserId, row.ResourceId));
    }

    [Fact]
    public async Task RejectingNeedsAReasonStoresItEmailsTheOwnerAndAllowsAResubmission()
    {
        var id = await AddDoctor();
        using var operations = Client(null, ops);
        Assert.Equal(HttpStatusCode.BadRequest, (await Decide(operations, h.Tenant, id, false, "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, false, "Number not found in the register")).StatusCode);

        var note = Assert.Single(mail.Sent, m => m.To == Contact);
        Assert.Contains("Number not found in the register", note.Body);
        using var admin = Client(Role.PracticeAdmin);
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.Equal(("Rejected", "Number not found in the register"), (listed.GetProperty("licence").GetString(), listed.GetProperty("rejectionReason").GetString()));
        await using (var audit = sql.Audit(h.Tenant))
            Assert.Single(await audit.Events.Where(e => e.Action == "licence.reject").ToListAsync());

        // Corrected and sent again: pending, the reason is gone; a verified licence cannot be edited.
        var resubmit = await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "84214", category = "Permanent", expiresOn = "2031-12-31" });
        Assert.Equal(HttpStatusCode.OK, resubmit.StatusCode);
        Assert.Equal("Pending", (await resubmit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("licence").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "99999", category = "Permanent", expiresOn = "2031-12-31" })).StatusCode);
    }

    [Fact]
    public async Task AFailedNotificationUndoesTheRejection()
    {
        var id = await AddDoctor();
        await using (var scope = sql.Context(h.Tenant))
        {
            var review = new LicenceReview(scope, new PostgresFixture.FixedTenant(h.Tenant), new Who(ops), new Medvya.Audit.Infrastructure.AuditLog(sql.Audit(h.Tenant)), new FailingMail(), TimeProvider.System);
            await Assert.ThrowsAsync<InvalidOperationException>(() => review.DecideAsync(id, false, "Not in the register"));
        }
        await using var db = sql.Context(h.Tenant);
        Assert.Null((await db.DoctorLicences.SingleAsync()).RejectedAt);
    }

    sealed class FailingMail : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => throw new InvalidOperationException("mail server down");
    }

    // ---- The gate: an unverified doctor cannot record or sign ----

    sealed class Who(Guid id) : ICurrentUser { public Guid? UserId => id; }
    sealed class At(Guid id) : ICurrentFacility { public Guid? FacilityId => id; }

    /// <summary>Mutation check: drop the doctor clause or the verified test from StaffAccess.LicencePendingAsync and this fails.</summary>
    [Fact]
    public async Task ADoctorWithoutAVerifiedLicenceCannotRecordOrSignWhetherPendingRejectedOrNeverSubmitted()
    {
        async Task<Access> Notes(Guid user)
        {
            await using var db = sql.Context(h.Tenant);
            return await new StaffAccess(db, new FacilityDirectory(db), new Who(user), new At(h.Facility)).AccessAsync(Capability.ClinicalNotes);
        }

        // SecondDoctor is an active hospital doctor with no licence row at all (invited by another route).
        Assert.Equal(Access.None, await Notes(h.SecondDoctor));
        var id = await AddDoctor();
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, h.Doctor).SetProperty(m => m.FacilityId, h.OtherFacility).SetProperty(m => m.Status, MembershipStatus.Active));
        async Task<Access> Own()
        {
            await using var db = sql.Context(h.Tenant);
            return await new StaffAccess(db, new FacilityDirectory(db), new Who(h.Doctor), new At(h.OtherFacility)).AccessAsync(Capability.ClinicalNotes);
        }
        Assert.Equal(Access.None, await Own()); // pending
        using var operations = Client(null, ops);
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, false, "Expired certificate")).StatusCode);
        Assert.Equal(Access.None, await Own()); // rejected
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, true)).StatusCode);
        Assert.Equal(Access.Sign, await Own()); // verified
        Assert.Equal(Access.None, await Notes(h.SecondDoctor)); // the doctor without a licence stays gated
    }

    /// <summary>MDV-P1-010. Mutation check: drop the expiry clause from StaffAccess.LicencePendingAsync and the expired step fails.</summary>
    [Fact]
    public async Task AnExpiredLicenceGatesTheDoctorAgainUntilRenewedAndVerifiedAgain()
    {
        var id = await AddDoctor();
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, h.Doctor).SetProperty(m => m.FacilityId, h.OtherFacility).SetProperty(m => m.Status, MembershipStatus.Active));
        async Task<Access> Own()
        {
            await using var db = sql.Context(h.Tenant);
            return await new StaffAccess(db, new FacilityDirectory(db), new Who(h.Doctor), new At(h.OtherFacility)).AccessAsync(Capability.ClinicalNotes);
        }
        using var operations = Client(null, ops);
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, true)).StatusCode);
        Assert.Equal(Access.Sign, await Own());

        // The expiry date is today: still valid. The day after: gated.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = sql.Context(h.Tenant))
            await db.DoctorLicences.ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresOn, today));
        Assert.Equal(Access.Sign, await Own());
        await using (var db = sql.Context(h.Tenant))
            await db.DoctorLicences.ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresOn, today.AddDays(-1)));
        Assert.Equal(Access.None, await Own());

        // Renewing sends it back to verification (an expired licence may be resubmitted; a valid verified one may not).
        using var admin = Client(Role.PracticeAdmin);
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.Equal("Expired", listed.GetProperty("licence").GetString());
        var renewed = await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "84213", category = "Permanent", expiresOn = today.AddYears(5).ToString("O", System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.Equal(Access.None, await Own()); // pending again
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(operations, h.Tenant, id, true)).StatusCode);
        Assert.Equal(Access.Sign, await Own());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "99999", category = "Permanent", expiresOn = today.AddYears(6).ToString("O", System.Globalization.CultureInfo.InvariantCulture) })).StatusCode); // verified and valid: not editable
    }

    // ---- Signature image ----

    [Fact]
    public async Task TheSignatureImageIsStoredPrivatelyIsNeverServedBackAndCanBeRemoved()
    {
        var id = await AddDoctor();
        using var admin = Client(Role.PracticeAdmin);
        var put = await admin.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var key = Assert.Single(h.Logos.Stored).Key;
        Assert.StartsWith($"{h.Tenant}/signature/", key);

        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.True(listed.GetProperty("hasSignature").GetBoolean());
        Assert.DoesNotContain("signatureKey", listed.ToString(), StringComparison.OrdinalIgnoreCase);
        // No read path: the image reaches nobody but a printed prescription (P2).
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync($"/v1/settings/doctors/{id}/signature")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent("<svg onload=alert(1)>"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/v1/settings/doctors/{id}/signature")).StatusCode);
        Assert.Empty(h.Logos.Stored);
        using var reception = Client(Role.Receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png))).StatusCode);
    }

    // ---- Schedule ----

    [Fact]
    public async Task ANewDoctorAppearsInTheScheduleColumnsWithHoursOnceTheyHaveJoinedAndTheirHoursAreSet()
    {
        var id = await AddDoctor();
        var user = Guid.NewGuid();
        // Joining is MDV-P0-013 (InvitationTests); here the invitation is taken as accepted.
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, user).SetProperty(m => m.Status, MembershipStatus.Active));
        await using (var identity = sql.Identity())
        {
            identity.Users.Add(new StaffUser { Id = user, Issuer = "test-idp", Subject = user.ToString(), DisplayName = "Dr. Sana Khan (synthetic)" });
            await identity.SaveChangesAsync();
        }

        var schedule = h.Schedule(h.Admin);
        var column = (await schedule.GetDayAsync(h.Facility, SchedulingHarness.Monday)).Columns.Single(c => c.CalendarId == user);
        Assert.Empty(column.Hours);

        await schedule.SaveTemplateAsync(new SaveTemplate
        {
            FacilityId = h.Facility, CalendarId = user, CalendarKind = Scheduling.Domain.CalendarKind.Doctor, SlotMinutes = 20,
            Hours = h.DoctorTemplate().Hours, Breaks = [], VisitLengths = [],
        });
        column = (await schedule.GetDayAsync(h.Facility, SchedulingHarness.Monday)).Columns.Single(c => c.CalendarId == user);
        Assert.NotEmpty(column.Hours);
        Assert.NotEmpty(column.Slots);
    }
}
