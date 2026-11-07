using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Host.Api.Auth;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-012: facility picker, /v1/me/context, server-side idle timeout and session revocation, with audit.</summary>
public sealed class SessionControlTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IDisposable
{
    readonly WebApplicationFactory<Program> factory = StaffSignInTests.CreateFactory(sql.ConnectionString);
    readonly Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid();
    Guid facilityA, facilityB;

    public void Dispose() => factory.Dispose();

    HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    async Task Clinics()
    {
        facilityA = await Clinic(tenantA, "Iyer Family Clinic (synthetic)", "clinic.alpha");
        facilityB = await Clinic(tenantB, "Lakeside Clinic (synthetic)", "clinic.beta");
    }

    async Task<Guid> Clinic(Guid tenant, string name, string flag)
    {
        await using var db = sql.Context(tenant);
        var facility = new Facility { Id = Guid.NewGuid(), TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" };
        db.Tenants.Add(new Tenant { Id = tenant, Name = name, CountryCode = "IN", ResidencyRegion = "centralindia" });
        db.Facilities.Add(facility);
        db.FeatureFlags.Add(new FeatureFlag { Id = Guid.NewGuid(), TenantId = tenant, Key = flag, Enabled = true, Owner = "platform", Description = "test" });
        db.FeatureFlags.Add(new FeatureFlag { Id = Guid.NewGuid(), TenantId = tenant, Key = flag + ".off", Enabled = false, Owner = "platform", Description = "test" });
        await db.SaveChangesAsync();
        return facility.Id;
    }

    async Task<StaffUser> Account(string name)
    {
        var id = Guid.NewGuid();
        var user = new StaffUser { Id = id, Issuer = IdentityProviders.Development, Subject = id.ToString(), DisplayName = $"{name} (synthetic)" };
        await using var db = sql.Identity();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    async Task<Guid> Member(StaffUser user, Guid tenant, Guid facility, Role role, MembershipStatus status = MembershipStatus.Active)
    {
        await using var db = sql.Context(tenant);
        var m = new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = user.Id, FacilityId = facility, Role = role, Status = status };
        db.Memberships.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    async Task<HttpClient> SignedIn(StaffUser user)
    {
        var browser = Browser();
        await StaffSignInTests.SignIn(browser, user.Subject);
        return browser;
    }

    static Task<HttpResponseMessage> Send(HttpClient browser, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(StaffAuthentication.CsrfHeader, "1");
        return browser.SendAsync(request);
    }

    static Task<HttpResponseMessage> Choose(HttpClient browser, Guid membershipId) => Send(browser, HttpMethod.Post, "/v1/me/facility", new { membershipId });

    static async Task<JsonElement> Context(HttpClient browser)
    {
        var res = await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    static async Task<HttpStatusCode> ContextStatus(HttpClient browser) => (await browser.GetAsync(new Uri("/v1/me/context", UriKind.Relative))).StatusCode;

    async Task<List<string>> AuditActions(Guid tenant, Guid actor)
    {
        await using var db = sql.Audit(tenant);
        return await db.Events.Where(e => e.ActorUserId == actor).OrderBy(e => e.OccurredAt).Select(e => e.Action).ToListAsync();
    }

    async Task<StaffSession> SessionOf(Guid userId)
    {
        await using var db = sql.Identity();
        return await db.Sessions.AsNoTracking().OrderByDescending(s => s.CreatedAt).FirstAsync(s => s.UserId == userId);
    }

    async Task Age(Guid sessionId, TimeSpan lastSeen, TimeSpan? created = null)
    {
        await using var db = sql.Identity();
        var now = DateTimeOffset.UtcNow;
        await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now - lastSeen));
        if (created is { } age) await db.Sessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CreatedAt, now - age));
    }

    [Fact]
    public async Task TwoMembershipsMeanChoosingAFacilityAndSwitchingWithoutSigningInAgain()
    {
        await Clinics();
        var user = await Account("A. Joseph");
        var atA = await Member(user, tenantA, facilityA, Role.Receptionist);
        var atB = await Member(user, tenantB, facilityB, Role.PracticeAdmin);
        using var browser = await SignedIn(user);

        var before = await Context(browser);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("active").ValueKind);
        var memberships = before.GetProperty("memberships").EnumerateArray().ToList();
        Assert.Equal(2, memberships.Count);
        var a = memberships.Single(m => m.GetProperty("id").GetGuid() == atA);
        Assert.Equal("Iyer Family Clinic (synthetic)", a.GetProperty("tenantName").GetString());
        Assert.Equal("Adyar (synthetic)", a.GetProperty("facilityName").GetString());
        Assert.Equal(tenantA, a.GetProperty("tenantId").GetGuid());
        Assert.Equal(facilityA, a.GetProperty("facilityId").GetGuid());
        Assert.Equal("Receptionist", a.GetProperty("role").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await Choose(browser, atA)).StatusCode);
        var inA = (await Context(browser)).GetProperty("active");
        Assert.Equal(atA, inA.GetProperty("membershipId").GetGuid());
        Assert.Equal(tenantA, inA.GetProperty("tenantId").GetGuid());
        Assert.Equal(facilityA, inA.GetProperty("facilityId").GetGuid());
        Assert.Equal("Receptionist", inA.GetProperty("role").GetString());
        Assert.Equal(["clinic.alpha"], inA.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("Asia/Kolkata", inA.GetProperty("timeZone").GetString()); // screens that show instants use the clinic's time zone

        // Switch: no provider round trip, just the facility endpoint on the same session.
        Assert.Equal(HttpStatusCode.NoContent, (await Choose(browser, atB)).StatusCode);
        var inB = (await Context(browser)).GetProperty("active");
        Assert.Equal(tenantB, inB.GetProperty("tenantId").GetGuid());
        Assert.Equal("PracticeAdmin", inB.GetProperty("role").GetString());
        Assert.Equal(["clinic.beta"], inB.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));

        // Audited in the tenant the user entered; each tenant sees only its own rows.
        Assert.Equal(["auth.signin"], await AuditActions(tenantA, user.Id));
        Assert.Equal(["auth.facility.switch"], await AuditActions(tenantB, user.Id));
        var session = await SessionOf(user.Id);
        Assert.Equal((tenantB, atB, 15), (session.TenantId, session.MembershipId, session.IdleTimeoutMinutes));
    }

    // MDV-P1-059: the picker puts the facility the user last worked at first; the server says which, from the user's own sessions.
    [Fact]
    public async Task ContextNamesTheLastUsedMembershipFromThePreviousSession()
    {
        await Clinics();
        var user = await Account("A. Joseph");
        var atA = await Member(user, tenantA, facilityA, Role.Receptionist);
        var atB = await Member(user, tenantB, facilityB, Role.Receptionist);

        using (var first = await SignedIn(user))
        {
            Assert.Equal(JsonValueKind.Null, (await Context(first)).GetProperty("lastMembershipId").ValueKind); // never chose one
            Assert.Equal(HttpStatusCode.NoContent, (await Choose(first, atB)).StatusCode);
        }
        using var second = await SignedIn(user);
        Assert.Equal(atB, (await Context(second)).GetProperty("lastMembershipId").GetGuid());

        // A membership that is no longer active is never named, even when it was used last.
        await using (var db = sql.Context(tenantB))
            await db.Memberships.Where(m => m.Id == atB).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MembershipStatus.Suspended));
        Assert.Equal(JsonValueKind.Null, (await Context(second)).GetProperty("lastMembershipId").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await Choose(second, atA)).StatusCode);
        Assert.Equal(atA, (await Context(second)).GetProperty("lastMembershipId").GetGuid());
    }

    [Fact]
    public async Task ContextJsonUsesTheAgreedShapeWithEnumNamesAsStrings()
    {
        await Clinics();
        var user = await Account("A. Joseph");
        using var browser = await SignedIn(user);
        await Choose(browser, await Member(user, tenantA, facilityA, Role.Receptionist));

        // The app shell (MDV-P0-022) reads these exact names; numbers or camelCase enum values would break it.
        var json = await browser.GetStringAsync(new Uri("/v1/me/context", UriKind.Relative));
        foreach (var fragment in new[] { "\"user\":{\"id\":", "\"displayName\":", "\"memberships\":[{\"id\":", "\"tenantName\":", "\"facilityName\":",
            "\"role\":\"Receptionist\"", "\"active\":{\"membershipId\":", "\"country\":\"IN\"", "\"capabilities\":{\"RegisterBookPatients\":\"Full\"", "\"CheckInQueue\":\"Full\"",
            "\"flags\":[\"clinic.alpha\"]" })
            Assert.Contains(fragment, json, StringComparison.Ordinal);
        Assert.DoesNotContain("ClinicalNotes", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Role.Receptionist)]
    [InlineData(Role.Nurse)]
    [InlineData(Role.PracticeAdmin)]
    [InlineData(Role.Patient)]
    public async Task ContextNeverReturnsCapabilitiesTheRoleDoesNotGrant(Role role)
    {
        await Clinics();
        var user = await Account("Priya N.");
        var membership = await Member(user, tenantA, facilityA, role);
        using var browser = await SignedIn(user);
        await Choose(browser, membership);

        var capabilities = (await Context(browser)).GetProperty("active").GetProperty("capabilities")
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        var expected = Enum.GetValues<Capability>().Where(c => RoleMatrix.Of(role, c) != Access.None)
            .ToDictionary(c => c.ToString(), c => (string?)RoleMatrix.Of(role, c).ToString());
        Assert.Equal(expected, capabilities);
        Assert.DoesNotContain(nameof(Access.None), capabilities.Values);
    }

    [Fact]
    public async Task OnlyOwnActiveMembershipsCanBeChosen()
    {
        await Clinics();
        var user = await Account("S. Thomas RN");
        var colleague = await Account("Dr. S. Menon");
        var suspended = await Member(user, tenantB, facilityB, Role.Nurse, MembershipStatus.Suspended);
        var someoneElses = await Member(colleague, tenantA, facilityA, Role.HospitalDoctor);
        using var browser = await SignedIn(user);

        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(browser, suspended)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(browser, someoneElses)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(browser, Guid.NewGuid())).StatusCode);
        var context = await Context(browser);
        Assert.Empty(context.GetProperty("memberships").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("active").ValueKind);
    }

    [Fact]
    public async Task IdleTimeoutEndsTheSessionAndBackgroundPollsDoNotKeepItAlive()
    {
        await Clinics();
        var user = await Account("K. Lakshmi");
        using var browser = await SignedIn(user);
        await Choose(browser, await Member(user, tenantA, facilityA, Role.PracticeAdmin));
        var session = await SessionOf(user.Id);

        // A passive request (the client's status poll) leaves last-seen alone; a real one moves it.
        await Age(session.Id, TimeSpan.FromMinutes(5));
        using (var poll = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/me/context", UriKind.Relative)))
        {
            poll.Headers.Add(SessionControl.PassiveHeader, "1");
            Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(poll)).StatusCode);
        }
        Assert.True(DateTimeOffset.UtcNow - (await SessionOf(user.Id)).LastSeenAt > TimeSpan.FromMinutes(4));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(browser));
        Assert.True(DateTimeOffset.UtcNow - (await SessionOf(user.Id)).LastSeenAt < TimeSpan.FromMinutes(1));

        await Age(session.Id, TimeSpan.FromMinutes(StaffSession.DefaultIdleTimeoutMinutes));
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(browser));
    }

    [Fact]
    public async Task TenantIdleTimeoutAppliesOnceAFacilityIsChosen()
    {
        await Clinics();
        await using (var db = sql.Context(tenantA))
        {
            (await db.Tenants.SingleAsync()).IdleTimeoutMinutes = 60;
            await db.SaveChangesAsync();
        }
        var user = await Account("R. Varghese");
        using var browser = await SignedIn(user);
        await Choose(browser, await Member(user, tenantA, facilityA, Role.HospitalIt));
        var session = await SessionOf(user.Id);
        Assert.Equal(60, session.IdleTimeoutMinutes);

        await Age(session.Id, TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(browser));
        await Age(session.Id, TimeSpan.Zero, created: StaffSession.MaxLifetime); // the 8 h cap holds whatever the activity
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(browser));
    }

    [Fact]
    public async Task RevokingAnotherOfMySessionsEndsItOnItsNextRequest()
    {
        await Clinics();
        var user = await Account("Dr. Arjun Rao");
        var membership = await Member(user, tenantA, facilityA, Role.SoloDoctor);
        using var desk = await SignedIn(user);
        await Choose(desk, membership);
        var deskSession = await SessionOf(user.Id);
        using var phone = await SignedIn(user);
        await Choose(phone, membership);

        var listed = await (await phone.GetAsync(new Uri("/v1/me/sessions", UriKind.Relative))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, listed.GetArrayLength());
        Assert.False(listed.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == deskSession.Id).GetProperty("current").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(phone, HttpMethod.Delete, $"/v1/me/sessions/{deskSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(desk));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(phone));
        Assert.Contains("session.revoke", await AuditActions(tenantA, user.Id));
    }

    [Fact]
    public async Task NobodyCanRevokeSomeoneElsesSessionAsTheirOwn()
    {
        await Clinics();
        var victim = await Account("A. Joseph");
        using var victimBrowser = await SignedIn(victim);
        var victimSession = await SessionOf(victim.Id);
        var other = await Account("Priya N.");
        using var browser = await SignedIn(other);
        await Choose(browser, await Member(other, tenantA, facilityA, Role.CareCoordinator));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(browser, HttpMethod.Delete, $"/v1/me/sessions/{victimSession.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(victimBrowser));
    }

    [Fact]
    public async Task TenantAdminEndsAStaffMembersSessionsInThatTenantOnly()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var staff = await Account("A. Joseph");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        var staffAtA = await Member(staff, tenantA, facilityA, Role.Receptionist);
        var staffAtB = await Member(staff, tenantB, facilityB, Role.Receptionist);
        using var staffInA = await SignedIn(staff);
        await Choose(staffInA, staffAtA);
        using var staffInB = await SignedIn(staff);
        await Choose(staffInB, staffAtB);

        // A receptionist is not a tenant admin.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staffInA, HttpMethod.Delete, $"/v1/staff/{admin.Id}/sessions")).StatusCode);
        using var adminBrowser = await SignedIn(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(adminBrowser, HttpMethod.Delete, $"/v1/staff/{staff.Id}/sessions")).StatusCode); // no facility chosen yet
        await Choose(adminBrowser, adminMembership);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(adminBrowser, HttpMethod.Delete, $"/v1/staff/{staff.Id}/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(staffInA));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(staffInB)); // the other clinic's session is not this admin's to end
        Assert.Contains("session.revoke", await AuditActions(tenantA, admin.Id));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(adminBrowser));
    }

    [Fact]
    public async Task ACookieCopiedBeforeASwitchOrSignOutNoLongerWorks()
    {
        await Clinics();
        var user = await Account("S. Thomas RN");
        var atA = await Member(user, tenantA, facilityA, Role.Nurse);
        var atB = await Member(user, tenantB, facilityB, Role.Nurse);
        using var browser = Browser();
        await StaffSignInTests.SignIn(browser, user.Subject);
        var chosen = await Choose(browser, atA);
        var copied = Assert.Single(chosen.Headers.GetValues("Set-Cookie"), c => c.StartsWith(StaffAuthentication.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];

        using var thief = Browser();
        thief.DefaultRequestHeaders.Add("Cookie", copied);
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(thief));

        await Choose(browser, atB); // the copy still names tenant A
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(thief));

        Assert.Equal(HttpStatusCode.NoContent, (await Send(browser, HttpMethod.Post, "/bff/logout")).StatusCode);
        Assert.Equal(["auth.signin"], await AuditActions(tenantA, user.Id));
        Assert.Equal(["auth.facility.switch", "auth.signout"], await AuditActions(tenantB, user.Id));
        Assert.NotNull((await SessionOf(user.Id)).RevokedAt);
    }

    // ---- Offboarding (MDV-P1-009) ----

    async Task<Guid> BookedVisitAsync(Guid doctor, Guid facility, DateTimeOffset start, bool slots = true)
    {
        var visit = new Medvya.Scheduling.Domain.Appointment
        {
            Id = Guid.NewGuid(), TenantId = tenantA, FacilityId = facility, CalendarId = doctor, CalendarKind = Medvya.Scheduling.Domain.CalendarKind.Doctor, PatientId = Guid.NewGuid(),
            VisitType = Medvya.Scheduling.Domain.VisitType.FollowUp, Start = start, End = start.AddMinutes(15), State = Medvya.Scheduling.Domain.AppointmentState.Booked,
            ReasonCategory = Medvya.Scheduling.Domain.ReasonCategory.General, Channel = Medvya.Scheduling.Domain.BookingChannel.Phone, BookedAt = DateTimeOffset.UtcNow, ChangedAt = DateTimeOffset.UtcNow,
        };
        await using var db = sql.Scheduling(tenantA);
        db.Appointments.Add(visit);
        if (slots) db.ScheduleSlots.AddRange(Medvya.Scheduling.Domain.ScheduleSlot.For(visit));
        await db.SaveChangesAsync();
        return visit.Id;
    }

    [Fact]
    public async Task OffboardingADoctorMovesTheirFutureVisitsToNeedsRebookingInTheSameTransaction()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var doctor = await Account("Dr. R. Menon");
        var other = await Account("Dr. S. Rao");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        var doctorMembership = await Member(doctor, tenantA, facilityA, Role.HospitalDoctor);
        var soon = DateTimeOffset.UtcNow.AddDays(3);
        var future = await BookedVisitAsync(doctor.Id, facilityA, soon);
        var past = await BookedVisitAsync(doctor.Id, facilityA, DateTimeOffset.UtcNow.AddDays(-3), slots: false);
        var others = await BookedVisitAsync(other.Id, facilityA, soon);
        using var adminBrowser = await SignedIn(admin);
        await Choose(adminBrowser, adminMembership);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{doctorMembership}/offboard", new { reason = "Left the practice" })).StatusCode);

        await using var db = sql.Scheduling(tenantA);
        Assert.Equal(Medvya.Scheduling.Domain.AppointmentState.NeedsRebooking, (await db.Appointments.SingleAsync(a => a.Id == future)).State);
        Assert.Equal(0, await db.ScheduleSlots.CountAsync(s => s.AppointmentId == future)); // the slot is free again
        Assert.Equal(Medvya.Scheduling.Domain.AppointmentState.Booked, (await db.Appointments.SingleAsync(a => a.Id == past)).State);
        Assert.Equal(Medvya.Scheduling.Domain.AppointmentState.Booked, (await db.Appointments.SingleAsync(a => a.Id == others)).State);
        Assert.Contains("appointment.needs_rebooking", await AuditActions(tenantA, admin.Id));
    }

    [Fact]
    public async Task IfMovingTheVisitsFailsTheOffboardingRollsBackAndTheDoctorStaysActive()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var doctor = await Account("Dr. R. Menon");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        var doctorMembership = await Member(doctor, tenantA, facilityA, Role.HospitalDoctor);
        using var doctorBrowser = await SignedIn(doctor);
        await Choose(doctorBrowser, doctorMembership);
        // A visit whose facility is not in this tenant cannot be placed in a time zone: the release fails after the membership was changed.
        var stuck = await BookedVisitAsync(doctor.Id, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(3));
        using var adminBrowser = await SignedIn(admin);
        await Choose(adminBrowser, adminMembership);

        using var failed = await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{doctorMembership}/offboard", new { reason = "Left the practice" });
        Assert.True((int)failed.StatusCode >= 500);

        await using var tenancy = sql.Context(tenantA);
        Assert.Equal(MembershipStatus.Active, (await tenancy.Memberships.SingleAsync(m => m.Id == doctorMembership)).Status);
        Assert.Empty(await tenancy.SettingsChanges.Where(c => c.Section == "staff.offboard").ToListAsync());
        Assert.DoesNotContain("staff.offboard", await AuditActions(tenantA, admin.Id));
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(doctorBrowser)); // sessions were not ended
        await using var db = sql.Scheduling(tenantA);
        Assert.Equal(Medvya.Scheduling.Domain.AppointmentState.Booked, (await db.Appointments.SingleAsync(a => a.Id == stuck)).State);
    }

    [Fact]
    public async Task OffboardingEndsTheSessionsInThatTenantAtOnceKeepsOtherClinicsAndRecordsWhy()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var staff = await Account("A. Joseph");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        var staffAtA = await Member(staff, tenantA, facilityA, Role.Receptionist);
        var staffAtB = await Member(staff, tenantB, facilityB, Role.Receptionist);
        using var staffInA = await SignedIn(staff);
        await Choose(staffInA, staffAtA);
        using var staffInB = await SignedIn(staff);
        await Choose(staffInB, staffAtB);
        using var adminBrowser = await SignedIn(admin);
        await Choose(adminBrowser, adminMembership);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(staffInA, HttpMethod.Post, $"/v1/staff/{adminMembership}/offboard", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{staffAtA}/offboard", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(staffInA)); // nothing changed without a reason

        Assert.Equal(HttpStatusCode.NoContent, (await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{staffAtA}/offboard", new { reason = "Left the practice" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(staffInA)); // the very next request
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(staffInB)); // another clinic is unaffected
        using var again = await SignedIn(staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await Choose(again, staffAtA)).StatusCode); // cannot sign in to that tenant any more
        Assert.Equal(HttpStatusCode.NotFound, (await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{staffAtA}/offboard", new { reason = "twice" })).StatusCode);

        await using var db = sql.Context(tenantA);
        Assert.Equal(MembershipStatus.Offboarded, (await db.Memberships.SingleAsync(m => m.Id == staffAtA)).Status);
        var change = await db.SettingsChanges.SingleAsync(c => c.Section == "staff.offboard");
        Assert.Equal((admin.Id, staffAtA), (change.ActorUserId, change.ResourceId));
        Assert.Contains("Left the practice", change.After);
        Assert.Contains("staff.offboard", await AuditActions(tenantA, admin.Id));
    }

    [Fact]
    public async Task OffboardingAnInviteeRevokesTheirPendingInvitation()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        var invited = Guid.NewGuid();
        await using (var db = sql.Context(tenantA))
        {
            db.Memberships.Add(new Membership { Id = invited, TenantId = tenantA, FacilityId = facilityA, Role = Role.Nurse, Status = MembershipStatus.Invited });
            db.Invitations.Add(new Invitation
            {
                Id = Guid.NewGuid(), TenantId = tenantA, MembershipId = invited, Email = "invitee@example.test", TokenHash = new byte[32], InvitedBy = admin.Id,
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            });
            await db.SaveChangesAsync();
        }
        using var adminBrowser = await SignedIn(admin);
        await Choose(adminBrowser, adminMembership);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{invited}/offboard", new { reason = "Offer withdrawn" })).StatusCode);
        await using var check = sql.Context(tenantA);
        Assert.NotNull((await check.Invitations.SingleAsync(i => i.MembershipId == invited)).RevokedAt);
    }

    [Fact]
    public async Task TheLastPracticeAdminCannotBeOffboardedButOneOfTwoCan()
    {
        await Clinics();
        var admin = await Account("K. Lakshmi");
        var second = await Account("R. Menon");
        var adminMembership = await Member(admin, tenantA, facilityA, Role.PracticeAdmin);
        using var adminBrowser = await SignedIn(admin);
        await Choose(adminBrowser, adminMembership);

        var refused = await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{adminMembership}/offboard", new { reason = "Leaving" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("last_admin", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, await ContextStatus(adminBrowser));

        await Member(second, tenantA, facilityA, Role.PracticeAdmin);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(adminBrowser, HttpMethod.Post, $"/v1/staff/{adminMembership}/offboard", new { reason = "Leaving" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ContextStatus(adminBrowser));
    }
}
