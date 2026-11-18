using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Medvya.Billing.Domain;
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
/// MDV-P1-007 and MDV-P1-065 on real PostgreSQL and through the real endpoints: the Add doctor wizard's server side (licence rules per
/// country, the owner's attestation, invitation, private signature image) and that no Medvya check stands between a doctor and signing.
/// </summary>
public sealed class DoctorTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    static readonly byte[] Png = TestImages.Png;
    const string Email = "sana.khan@example.test", Contact = "desk@clinic.example";

    readonly SchedulingHarness h = new(sql);
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

    object Body(string? number = "84213", string authority = "TNMC", string? expiresOn = "2031-12-31", string? category = "Permanent", string email = Email, bool attested = true) => new
    {
        fullName = "Dr. Sana Khan (synthetic)", email, specialty = "Paediatrics", qualifications = "MBBS, MD", languages = "English, Hindi", facilityId = h.Facility,
        authority, licenceNumber = number, category, expiresOn, attested, newVisitFee = 50000, followUpFee = 30000,
    };

    async Task<Guid> AddDoctor(object? body = null)
    {
        using var admin = Client(Role.PracticeAdmin);
        var response = await admin.PostAsJsonAsync("/v1/settings/doctors", body ?? Body());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

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
        Assert.Equal(("TNMC", "Permanent", new DateOnly(2031, 12, 31), (Guid?)h.Admin), (licence.Authority, licence.Category, licence.ExpiresOn, licence.AttestedBy));
        Assert.InRange(licence.AttestedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        // The audit holds who attested and the values attested (append-only settings change).
        var change = await db.SettingsChanges.SingleAsync(c => c.Section == "doctor" && c.ResourceId == id);
        Assert.Equal((h.Admin, true, true, true), (change.ActorUserId, change.After!.Contains("84213"), change.After.Contains("2031-12-31"), change.After.Contains("Attested")));
        Assert.Equal("84213", JsonDocument.Parse(change.After).RootElement.GetProperty("number").GetString()); // serialised, never pasted into a JSON string
        var profile = await db.DoctorProfiles.SingleAsync(p => p.MembershipId == id);
        Assert.Null(profile.SignatureKey);
        // The fees are the doctor's own prices in the Billing catalogue (MDV-P4-002), saved with the membership; Tenancy keeps no copy.
        Assert.Equal((0L, 0L), (profile.NewVisitFeeMinor, profile.FollowUpFeeMinor));
        await using (var billing = sql.Billing(h.Tenant))
        {
            var prices = await billing.PriceVersions.Where(v => v.DoctorId == id).Join(billing.Services, v => v.ServiceId, s => s.Id, (v, s) => new { s.ConsultationKind, v.AmountMinor, v.Currency }).ToListAsync();
            Assert.Equal([(ConsultationKind.FollowUp, 30000L, "INR"), (ConsultationKind.New, 50000L, "INR")],
                prices.Select(p => (p.ConsultationKind!.Value, p.AmountMinor, p.Currency)).OrderBy(p => p.Item1.ToString()));
        }
        Assert.Single(await db.Invitations.Where(i => i.MembershipId == id).ToListAsync());
        Assert.Contains("/invite#t=", Assert.Single(mail.Sent, m => m.To == Email).Body);
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
    [InlineData("attested", "required", "84213", "TNMC", "2031-12-31", "Permanent", false)] // MDV-P1-065: the owner must attest; mutation check: drop the check in Doctors.AddAsync
    public async Task ALicenceWithABadNumberAuthorityCategoryExpiryOrNoAttestationIsRefusedAndNothingIsCreated(string field, string code, string number, string authority, string? expiresOn, string category, bool attested = true)
    {
        using var admin = Client(Role.PracticeAdmin);
        var response = await admin.PostAsJsonAsync("/v1/settings/doctors", Body(number, authority, expiresOn, category, attested: attested));
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

    // ---- Attestation: the owner answers for the licence, Medvya does not check it (ADR-016) ----

    sealed class Who(Guid id) : ICurrentUser { public Guid? UserId => id; }
    sealed class At(Guid id) : ICurrentFacility { public Guid? FacilityId => id; }

    async Task<Access> NotesAsync(Guid user, Guid facility)
    {
        await using var db = sql.Context(h.Tenant);
        return await new StaffAccess(db, new FacilityDirectory(db, new NoLogoStore()), new Who(user), new At(facility)).AccessAsync(Capability.ClinicalNotes);
    }

    /// <summary>
    /// MDV-P8-008: a dentist is added like any doctor with a dental council licence (no medical council needed), keeps a doctor's clinical access and no
    /// more, prints the dental council on prescriptions and signed notes, and their licence expiry is watched like any other.
    /// SAFETY (mutation check: drop the dental councils from the India list, or let the print fall back to the code, and this fails).
    /// </summary>
    [Fact]
    public async Task ADentistIsADoctorWithADentalLicenceThatPrintsAndIsWatched()
    {
        var expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);
        var id = await AddDoctor(Body(authority: "DCI", number: "A-12345", expiresOn: expires.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, h.Doctor).SetProperty(m => m.FacilityId, h.OtherFacility).SetProperty(m => m.Status, MembershipStatus.Active));

        using var admin = Client(Role.PracticeAdmin);
        var list = await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors");
        var dci = list.GetProperty("authorities").EnumerateArray().Single(a => a.GetProperty("code").GetString() == "DCI");
        Assert.Equal(("Dental Council of India", true), (dci.GetProperty("name").GetString(), dci.GetProperty("dental").GetBoolean()));
        Assert.False(list.GetProperty("authorities").EnumerateArray().Single(a => a.GetProperty("code").GetString() == "NMC").GetProperty("dental").GetBoolean());

        // The same clinical access as a doctor, and no more: every capability equals a medical doctor's in the same role.
        await using (var db = sql.Context(h.Tenant))
            foreach (var capability in Enum.GetValues<Capability>())
            {
                var dentist = await new StaffAccess(db, new FacilityDirectory(db, new NoLogoStore()), new Who(h.Doctor), new At(h.OtherFacility)).AccessAsync(capability);
                Assert.Equal(RoleMatrix.Of(Role.HospitalDoctor, capability), dentist);
            }

        await using (var db = sql.Context(h.Tenant))
        {
            var printed = await new FacilityDirectory(db, new NoLogoStore()).MembershipPrescriberAsync(id);
            Assert.Equal(("A-12345", "Dental Council of India"), (printed!.LicenceNumber, printed.LicenceAuthority)); // "Reg. A-12345 · Dental Council of India"
            var facts = await new ComplianceFactsReader(db).ReadAsync(DateOnly.FromDateTime(DateTime.UtcNow));
            Assert.Contains(facts.Licences, l => l.ExpiresOn == expires);
        }
    }

    [Fact]
    public async Task TheMedvyaLicenceReviewEndpointIsGone()
    {
        var id = await AddDoctor();
        using var anyone = Client(Role.PracticeAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.PostAsJsonAsync($"/v1/operations/licences/{h.Tenant}/{id}/decision", new { verified = true })).StatusCode);
    }

    /// <summary>Recording and signing no longer wait for a licence check: a new doctor, and one with no licence row, can sign.</summary>
    [Fact]
    public async Task AnAttestedDoctorCanSignWithNoMedvyaCheckAndTheListSaysWhoAttestedAndWhen()
    {
        var id = await AddDoctor();
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, h.Doctor).SetProperty(m => m.FacilityId, h.OtherFacility).SetProperty(m => m.Status, MembershipStatus.Active));
        Assert.Equal(Access.Sign, await NotesAsync(h.Doctor, h.OtherFacility));
        Assert.Equal(Access.Sign, await NotesAsync(h.SecondDoctor, h.Facility)); // no licence row at all: the organisation, not Medvya, answers for its doctors

        using var admin = Client(Role.PracticeAdmin);
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.Equal(h.Admin, listed.GetProperty("attestedBy").GetGuid());
        Assert.False(listed.GetProperty("expired").GetBoolean());
        Assert.False(listed.TryGetProperty("rejectionReason", out _));
    }

    /// <summary>MDV-P1-010 and ADR-016: an expired licence warns (the list says expired) and the owner re-attests; signing is not blocked.</summary>
    [Fact]
    public async Task AnExpiredLicenceWarnsAndIsReAttestedWithoutBlockingTheDoctor()
    {
        var id = await AddDoctor();
        await using (var db = sql.Context(h.Tenant))
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, h.Doctor).SetProperty(m => m.FacilityId, h.OtherFacility).SetProperty(m => m.Status, MembershipStatus.Active));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = sql.Context(h.Tenant))
            await db.DoctorLicences.ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresOn, today.AddDays(-1)));
        Assert.Equal(Access.Sign, await NotesAsync(h.Doctor, h.OtherFacility));
        await using (var db = sql.Context(h.Tenant)) // re-attesting is staff administration at the doctor's location
        {
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = h.Tenant, UserId = h.Admin, FacilityId = h.OtherFacility, Role = Role.PracticeAdmin, Status = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }

        using var admin = Client(Role.PracticeAdmin);
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.True(listed.GetProperty("expired").GetBoolean());
        var expires = today.AddYears(5).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "84213", category = "Permanent", expiresOn = expires })).StatusCode); // no attestation
        var renewed = await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "84213", category = "Permanent", expiresOn = expires, attested = true });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.False((await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expired").GetBoolean());
        await using var check = sql.Context(h.Tenant);
        Assert.Equal(2, await check.SettingsChanges.CountAsync(c => c.Section == "doctor" && c.ResourceId == id)); // add, then re-attest
    }

    // SAFETY: mutation check: made Doctors.AdministersAsync always true (licence, room, remove signature)
    // -> AnAdministratorOfAnotherLocationCannotChangeTheDoctor failed.
    [Fact]
    public async Task AnAdministratorOfAnotherLocationCannotChangeTheDoctor()
    {
        var id = await AddDoctor();
        var doctor = await Joined(id, Role.HospitalDoctor);
        using (var own = Client(null, doctor))
            Assert.Equal(HttpStatusCode.NoContent, (await own.PutAsync("/v1/me/signature", new ByteArrayContent(Png))).StatusCode);
        await using (var db = sql.Context(h.Tenant)) // the doctor works at Velachery, which h.Admin does not administer
            await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.FacilityId, h.OtherFacility));

        using var admin = Client(Role.PracticeAdmin);
        var expires = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/licence", new { authority = "TNMC", number = "99999", category = "Permanent", expiresOn = expires, attested = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/v1/settings/doctors/{id}/room", new { room = "Room 9" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/v1/settings/doctors/{id}/signature")).StatusCode);

        Assert.Single(h.Logos.Stored);
        await using var check = sql.Context(h.Tenant);
        Assert.Equal(("84213", null), (await check.DoctorLicences.Where(l => l.MembershipId == id).Select(l => l.Number).SingleAsync(),
            await check.DoctorProfiles.Where(p => p.MembershipId == id).Select(p => p.Room).SingleAsync()));
    }

    // ---- Signature image ----

    /// <summary>The added doctor has accepted the invitation (MDV-P0-013, InvitationTests) and administers the practice, as an owner doctor does.</summary>
    async Task<Guid> Joined(Guid id, Role role = Role.SoloDoctor)
    {
        var user = Guid.NewGuid();
        await using var db = sql.Context(h.Tenant);
        await db.Memberships.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UserId, user).SetProperty(m => m.Status, MembershipStatus.Active).SetProperty(m => m.Role, role));
        return user;
    }

    // SAFETY mutation check: remove the own-membership check in Doctors.SetSignatureAsync and the administrator's upload is stored.
    [Fact]
    public async Task OnlyTheDoctorSetsTheirOwnSignatureImage()
    {
        var id = await AddDoctor();
        using var admin = Client(Role.PracticeAdmin);
        // An administrator does not put an image on a doctor's prescriptions: not before the doctor has joined, and not after.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png))).StatusCode);
        var doctor = await Joined(id);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png))).StatusCode);
        Assert.Empty(h.Logos.Stored);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single().GetProperty("ownSignature").GetBoolean());

        using var own = Client(null, doctor);
        Assert.Equal(HttpStatusCode.NoContent, (await own.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png))).StatusCode);
        Assert.Single(h.Logos.Stored);
        var mine = (await own.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.True(mine.GetProperty("ownSignature").GetBoolean() && mine.GetProperty("hasSignature").GetBoolean());

        // An administrator may remove it, and the access log names them.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/v1/settings/doctors/{id}/signature")).StatusCode);
        Assert.Empty(h.Logos.Stored);
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal([("settings.doctor.signature.update", (Guid?)doctor), ("settings.doctor.signature.delete", h.ByRole[Role.PracticeAdmin])],
            (await audit.Events.Where(e => e.Action.StartsWith("settings.doctor.signature")).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync()).Select(e => (e.Action, e.ActorUserId)));
    }

    [Fact]
    public async Task TheSignatureImageIsStoredPrivatelyIsNeverServedBackAndCanBeRemoved()
    {
        var id = await AddDoctor();
        using var admin = Client(Role.PracticeAdmin);
        using var own = Client(null, await Joined(id));
        var put = await own.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var key = Assert.Single(h.Logos.Stored).Key;
        Assert.StartsWith($"{h.Tenant}/signature/", key);

        var listed = (await admin.GetFromJsonAsync<JsonElement>("/v1/settings/doctors")).GetProperty("doctors").EnumerateArray().Single();
        Assert.True(listed.GetProperty("hasSignature").GetBoolean());
        Assert.DoesNotContain("signatureKey", listed.ToString(), StringComparison.OrdinalIgnoreCase);
        // No read path: the image reaches nobody but a printed prescription (P2).
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync($"/v1/settings/doctors/{id}/signature")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await own.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent("<svg onload=alert(1)>"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/v1/settings/doctors/{id}/signature")).StatusCode);
        Assert.Empty(h.Logos.Stored);
        using var reception = Client(Role.Receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, (await reception.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png))).StatusCode);
    }

    // #1365: an employed doctor does not administer the practice, so the settings endpoint refuses them; their own endpoint does not.
    // SAFETY mutation check: drop the user match in Doctors.OwnSignatureAsync and the nurse's upload reaches the doctor's profile.
    [Fact]
    public async Task AnEmployedDoctorSetsAndRemovesTheirOwnSignatureImageAndNobodyElseDoesItForThem()
    {
        var id = await AddDoctor();
        var doctor = await Joined(id, Role.HospitalDoctor);
        using var own = Client(null, doctor);
        Assert.Equal(HttpStatusCode.Forbidden, (await own.PutAsync($"/v1/settings/doctors/{id}/signature", new ByteArrayContent(Png))).StatusCode);
        Assert.False((await own.GetFromJsonAsync<JsonElement>("/v1/me/signature")).GetProperty("hasSignature").GetBoolean());

        // Staff who do not prescribe have no such endpoint, and a prescriber without a doctor's profile here has nothing to set.
        using var nurse = Client(Role.Nurse);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.PutAsync("/v1/me/signature", new ByteArrayContent(Png))).StatusCode);
        using var other = Client(Role.SoloDoctor);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsync("/v1/me/signature", new ByteArrayContent(Png))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync("/v1/me/signature")).StatusCode);
        Assert.Empty(h.Logos.Stored);

        Assert.Equal(HttpStatusCode.BadRequest, (await own.PutAsync("/v1/me/signature", new ByteArrayContent("<svg onload=alert(1)>"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await own.PutAsync("/v1/me/signature", new ByteArrayContent(Png))).StatusCode);
        Assert.StartsWith($"{h.Tenant}/signature/", Assert.Single(h.Logos.Stored).Key);
        Assert.True((await own.GetFromJsonAsync<JsonElement>("/v1/me/signature")).GetProperty("hasSignature").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await own.DeleteAsync("/v1/me/signature")).StatusCode);
        Assert.Empty(h.Logos.Stored);
        await using var audit = sql.Audit(h.Tenant);
        Assert.Equal([("settings.doctor.signature.update", (Guid?)doctor), ("settings.doctor.signature.delete", doctor)],
            (await audit.Events.Where(e => e.Action.StartsWith("settings.doctor.signature")).OrderBy(e => e.OccurredAt).ThenBy(e => e.Id).ToListAsync()).Select(e => (e.Action, e.ActorUserId)));
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
