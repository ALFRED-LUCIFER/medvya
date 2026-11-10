using System.Security.Claims;
using Medvya.Audit.Api;
using Medvya.Tenancy.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>
/// MDV-P0-017: enumerates every endpoint in the host. Each signed-in endpoint is either <c>.Audited(...)</c> (patient data)
/// or reviewed below as never touching patient data, so a new endpoint fails here until someone decides (fail closed).
/// Each audited endpoint is then called against real SQL Server once per staff role, so both the allowed path (the endpoint
/// filter, then the handler) and the refused path (denied-attempt audit) run, and every request must write exactly one audit row.
/// </summary>
public sealed class EndpointAuditTests(PostgresFixture sql) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    /// <summary>Signed-in endpoints reviewed as never reading or writing patient data. Adding one here is a reviewed decision.</summary>
    // shortcut: kept in the test, not as metadata on the endpoints, while StaffAuthentication.cs is reworked (MDV-P0-012); move to endpoint metadata if the list grows.
    static readonly HashSet<string> NoPatientData =
    [
        "POST /bff/logout",     // ends the staff session
        "POST /bff/reauth",     // step-up before signing: starts a provider redirect, touches no patient data
        "GET /v1/me/context",   // the signed-in staff user, memberships and permissions
        // Session control (MDV-P0-012): staff sessions only; each change writes its own auth/session audit event (SessionControl).
        "POST /v1/me/facility",
        "GET /v1/me/sessions",
        "DELETE /v1/me/sessions/{id:guid}",
        "DELETE /v1/staff/{userId:guid}/sessions",
        // Sign-up wizard (MDV-P1-004): the person's own draft, before any tenant or patient exists; creating the practice is audited in it.
        "GET /v1/onboarding/",
        "POST /v1/onboarding/",
        "PUT /v1/onboarding/",
        "POST /v1/onboarding/finish",   // audited as onboarding.complete.{country} inside the new tenant
        "GET /v1/invitations/facilities",  // facility ids and names of the admin's tenant (MDV-P0-013)
        "POST /v1/invitations/",            // invites a staff member by email; audited as invitation.create inside the service
        "DELETE /v1/invitations/{id:guid}", // revokes a staff invitation; audited as invitation.revoke inside the service
        // Settings -> Staff (MDV-P1-008): staff names and roles, no patient data; invitations and role changes are audited inside the services.
        "GET /v1/staff/",
        "POST /v1/staff/",
        "POST /v1/staff/invitations/{id:guid}/resend",
        "GET /v1/staff/roles",
        "POST /v1/staff/roles",
        "PUT /v1/staff/roles/{id:guid}",
        "GET /v1/staff/access-review", // MDV-P1-010: staff names, roles, last sign-in; decisions are audited in the service
        "POST /v1/staff/access-review/{membershipId:guid}/confirm",
        "POST /v1/staff/{membershipId:guid}/offboard", // MDV-P1-009: audited as staff.offboard in the same transaction (SessionControlTests)
        "POST /v1/staff/{membershipId:guid}/reactivate", // MDV-P1-009: audited as staff.reactivate in the same transaction (SessionControlTests)
        // Practice settings (MDV-P1-005): business details only; every change is audited inside the service with before/after (PracticeSettingsTests).
        "GET /v1/settings/profile",
        "PUT /v1/settings/profile",
        "GET /v1/settings/locations",
        "POST /v1/settings/locations",
        "PUT /v1/settings/locations/{id:guid}",
        "GET /v1/settings/locations/{id:guid}/hours",
        "PUT /v1/settings/locations/{id:guid}/hours",
        "GET /v1/settings/locations/{id:guid}/closures", // visit references and times, no patient data
        "POST /v1/settings/locations/{id:guid}/closures",
        "DELETE /v1/settings/closures/{id:guid}",
        "PUT /v1/settings/logo",
        "DELETE /v1/settings/logo",
        "GET /v1/settings/logo",
        "GET /v1/settings/terms", // tenant terms status, no patient data
        "POST /v1/settings/terms/accept", // audited in its own transaction (tenant.terms.accept), no patient data
        "GET /v1/settings/clinical-access", // the clinic's policy (MDV-P2-024), no patient data
        "PUT /v1/settings/clinical-access", // audited with before/after (settings.clinical_access.update), no patient data
        "GET /v1/settings/nurse-intake", // the clinic's switch, no patient data
        "GET /v1/note-templates/", // MDV-P2-025: templates and the doctor's own choice, no patient data (NoteTemplateTests)
        "PUT /v1/note-templates/mine",
        "PUT /v1/settings/nurse-intake", // audited by the flag service (flag.enable / flag.disable), no patient data
        "POST /v1/chart/allergy-flags", // MDV-P2-002: one patient.chart.allergy-flag event per patient answered, written by the use case (PatientChartTests)
        // Add doctor (MDV-P1-007): clinician details, licence and fees, no patient data; changes are audited inside the services (DoctorTests).
        "GET /v1/settings/doctors/",
        "POST /v1/settings/doctors/",
        "PUT /v1/settings/doctors/{id:guid}/licence",
        "PUT /v1/settings/doctors/{id:guid}/signature",
        "DELETE /v1/settings/doctors/{id:guid}/signature",
        // Onboarding request (MDV-P1-062): the organisation's type, IT contact and documents, no patient data; every change is audited inside the service (OrganisationRequestTests).
        "GET /v1/organisation/request/",
        "PUT /v1/organisation/request/it-contact",
        "PUT /v1/organisation/request/documents/{kind}",
        "POST /v1/organisation/request/submit",
        // Medvya operations' approval API (MDV-P1-064): control-plane requests and documents only, never clinic data; each decision is audited inside the organisation (OrganisationApprovalTests).
        "GET /v1/operations/organisations/",
        "GET /v1/operations/organisations/{tenantId:guid}",
        "GET /v1/operations/organisations/{tenantId:guid}/documents/{documentId:guid}",
        "POST /v1/operations/organisations/{tenantId:guid}/reinstate",
        "POST /v1/operations/organisations/{tenantId:guid}/approve",
        "POST /v1/operations/organisations/{tenantId:guid}/request-changes",
        "POST /v1/operations/organisations/{tenantId:guid}/reject",
        "POST /v1/operations/organisations/{tenantId:guid}/suspend",
        // Live board hub (MDV-P1-029): opening the connection carries no patient data. A refused connect is audited Denied
        // (.AuditRefusals, front_desk.board.connect); every Watch call is audited Allowed or Denied by the hub (front_desk.board.watch);
        // messages are ids and state only, and the board itself is read through the audited GET /v1/front-desk/board (BoardHubTests).
        "GET /v1/hubs/board/negotiate", // SignalR maps negotiate without method metadata; it answers POST only
        "GET /v1/hubs/board",
        // Notification centre (MDV-P1-034): kinds, appointment ids and read marks only, no names or message text (NotificationCentreTests).
        "GET /v1/notifications/inbox",
        "POST /v1/notifications/inbox/read-all",
        "POST /v1/notifications/inbox/{id:guid}/read",
        "GET /v1/notifications/failed-visits",
        // Settings -> Notifications (MDV-P1-006): practice preferences and template previews with placeholders only; a save is audited as settings.notifications.update (NotificationsTests).
        "GET /v1/settings/notifications/",
        "PUT /v1/settings/notifications/",
        "GET /v1/settings/notifications/templates",
        // Queue TV (MDV-P1-031): display sessions only, never staff. Tokens, rooms, doctor display names and waits, no patient
        // data (QueueTvTests pins the fields); the hub's message is empty. Pairing a display is audited (queue_display.pair).
        // Formulary and order sets (MDV-P2-014): medicine names, classes and test names, no patient data; an add is audited inside the service (formulary.add, orderset.create; FormularyTests).
        "GET /v1/formulary",
        "GET /v1/order-sets",
        "GET /v1/settings/formulary",
        "POST /v1/settings/formulary",
        "GET /v1/settings/order-sets",
        "POST /v1/settings/order-sets",
        "GET /v1/queue/tv",
        "GET /v1/queue/hub/negotiate",
        "GET /v1/queue/hub",
    ];

    readonly Guid tenant = Guid.NewGuid(), facility = Guid.NewGuid();
    readonly Dictionary<Role, Guid> staff = Enum.GetValues<Role>().ToDictionary(r => r, _ => Guid.NewGuid());

    /// <summary>One active member per role at one facility of a synthetic tenant, so every capability policy has a member it lets through.</summary>
    public async Task InitializeAsync()
    {
        await using var db = sql.Context(tenant);
        db.Tenants.Add(new Tenant { State = OrganisationState.Approved, Id = tenant, Name = "Audit Enumeration Clinic (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
        db.Facilities.Add(new Facility { Id = facility, TenantId = tenant, Name = "Adyar (synthetic)", TimeZone = "Asia/Kolkata" });
        foreach (var (role, user) in staff)
        {
            var membership = new Membership { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, FacilityId = facility, Role = role, Status = MembershipStatus.Active };
            db.Memberships.Add(membership);
            // A verified solo doctor: signing capabilities (merge approval) are off until the licence is verified, so the allowed path needs one.
            if (role == Role.SoloDoctor)
                db.DoctorLicences.Add(new DoctorLicence { MembershipId = membership.Id, TenantId = tenant, Authority = "TNMC", Number = "84213", SubmittedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static IEnumerable<RouteEndpoint> Endpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>();

    static IEnumerable<string> Methods(RouteEndpoint e) => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"];

    static string Name(RouteEndpoint e, string method) => $"{method} {e.RoutePattern.RawText}";

    /// <summary>Signed-in endpoints that are neither audited nor reviewed as free of patient data.</summary>
    internal static List<string> Unclassified(WebApplicationFactory<Program> factory) =>
        Endpoints(factory)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetMetadata<AuditedEndpointMetadata>() is null)
            .SelectMany(e => Methods(e).Select(m => Name(e, m)))
            .Where(n => !NoPatientData.Contains(n))
            .ToList();

    /// <summary>A request to an audited endpoint: who sent it, how many audit rows it wrote, their outcomes and the HTTP status.</summary>
    internal sealed record Call(Role Role, int Rows, int Status, IReadOnlyList<Medvya.Audit.Domain.AuditOutcome> Outcomes)
    {
        /// <summary>401/403 means authorization refused it; anything else went through the endpoint filter to the handler.</summary>
        public bool Allowed => Status is not (401 or 403);
    }

    /// <summary>Calls every audited endpoint once per staff role and returns, per endpoint, what each request did.</summary>
    async Task<Dictionary<string, List<Call>>> AuditRowsPerRequest(WebApplicationFactory<Program> factory)
    {
        var calls = new Dictionary<string, List<Call>>();
        foreach (var (role, user) in staff)
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            client.DefaultRequestHeaders.Add(SignedIn.UserHeader, user.ToString());
            foreach (var endpoint in Endpoints(factory).Where(e => e.Metadata.GetMetadata<AuditedEndpointMetadata>() is not null))
            {
                foreach (var method in Methods(endpoint))
                {
                    // shortcut: route values are fresh ids and bodies are empty JSON unless SampleBody has one; add a synthetic sample body there when an endpoint needs one.
                    var name = Name(endpoint, method);
                    using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(Path(endpoint.RoutePattern), UriKind.Relative));
                    if (method is not ("GET" or "HEAD" or "DELETE")) request.Content = new StringContent(SampleBody(name, role), System.Text.Encoding.UTF8, "application/json");
                    var before = await Ids();
                    using var response = await client.SendAsync(request);
                    if (!calls.TryGetValue(name, out var list)) calls[name] = list = [];
                    var written = await NewOutcomes(before);
                    list.Add(new(role, written.Count, (int)response.StatusCode, written));
                }
            }
        }
        return calls;
    }

    /// <summary>
    /// A synthetic body that gets through validation, for endpoints whose use case audits only what it actually does
    /// (<c>.AuditedByUseCase</c>): with an empty body they refuse the input before any patient data is touched, and the
    /// allowed path, where the use case must write its event, would never run. One distinct synthetic person per role, so no
    /// call stops at a duplicate warning.
    /// </summary>
    static readonly string[] WritesWithSampleBodies = ["PUT /v1/schedule/templates", "POST /v1/schedule/resources", "POST /v1/slot-holds/", "POST /v1/patients/", "POST /v1/queue/displays/", "POST /v1/schedule/metrics/baselines"];

    string SampleBody(string endpoint, Role role) => endpoint switch
    {
        // Scheduling writes audit in their own transaction (MDV-P0-017): a template and a resource each role may add, and one
        // free slot per role next Monday in the solo doctor's calendar (the template is saved first, endpoints run in map order).
        "PUT /v1/schedule/templates" => System.Text.Json.JsonSerializer.Serialize(new
        {
            facilityId = facility, calendarId = staff[Role.SoloDoctor], calendarKind = "Doctor", slotMinutes = 20,
            hours = new[] { new { weekday = 1, start = "09:00:00", end = "13:00:00", room = "Room 3" } }, breaks = Array.Empty<object>(), visitLengths = Array.Empty<object>(),
        }),
        "POST /v1/schedule/resources" => System.Text.Json.JsonSerializer.Serialize(new { facilityId = facility, name = $"{role} room (synthetic)" }),
        "POST /v1/queue/displays/" => System.Text.Json.JsonSerializer.Serialize(new { facilityId = facility, name = $"{role} waiting room TV (synthetic)" }),
        "POST /v1/schedule/metrics/baselines" => System.Text.Json.JsonSerializer.Serialize(new { facilityId = facility, metric = "NoShowPercent", value = 15, method = "Paper register (synthetic)", fromDate = "2026-10-05", toDate = "2026-10-18" }),
        "POST /v1/slot-holds/" => System.Text.Json.JsonSerializer.Serialize(new
        {
            facilityId = facility, calendarId = staff[Role.SoloDoctor], visitType = "FollowUp", start = NextMondayIst(9).AddMinutes(20 * (int)role),
        }),
        // A walk-in for a patient this facility does not have: refused as Rejected (400) by the roles that may book, Denied by the rest.
        "POST /v1/appointments/walk-in" => System.Text.Json.JsonSerializer.Serialize(new { facilityId = facility, patientId = Guid.NewGuid(), visitType = "FollowUp", reasonCategory = "General" }),
        "POST /v1/patients/" => System.Text.Json.JsonSerializer.Serialize(new
        {
            consents = Medvya.IntegrationTests.PatientRulesTests.TreatmentConsentBody, quick = true, fullName = $"{role} Audit Probe (synthetic)", dateOfBirth = $"{1950 + (int)role}-02-0{1 + (int)role % 9}",
            sexAtBirth = "Female", mobile = $"+91 90000 09{(int)role:D3}",
        }),
        _ => "{}",
    };

    /// <summary>A wall-clock hour next Monday in IST, always in the future on the real clock the host runs on.</summary>
    static DateTimeOffset NextMondayIst(int hour)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 7);
        return new DateTimeOffset(monday.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(5.5));
    }

    async Task<HashSet<Guid>> Ids()
    {
        await using var db = sql.Audit(tenant);
        return [.. await db.Events.Select(e => e.Id).ToListAsync()];
    }

    async Task<List<Medvya.Audit.Domain.AuditOutcome>> NewOutcomes(HashSet<Guid> before)
    {
        await using var db = sql.Audit(tenant);
        return (await db.Events.Select(e => new { e.Id, e.Outcome }).ToListAsync()).Where(e => !before.Contains(e.Id)).Select(e => e.Outcome).ToList();
    }

    static string Path(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(s => string.Concat(s.Parts.Select(p => p switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart => Guid.NewGuid().ToString(),
            _ => "",
        }))));

    WebApplicationFactory<Program> Host(Action<IEndpointRouteBuilder>? extra = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Medvya", sql.ConnectionString)
            .UseSetting("RateLimiting:PermitsPerMinute", "100000") // this test calls every endpoint once per role: more than the per-IP limit of production traffic
            .ConfigureServices(s => s.AddSingleton<IStartupFilter>(new SignedIn(tenant, facility, extra))));

    /// <summary>The synthetic staff user named by <see cref="UserHeader"/>, in the test tenant and facility, plus optional extra endpoints.</summary>
    // shortcut: sets the tenant and facility claims directly because sign-in does not issue them before the facility picker (MDV-P0-012).
    sealed class SignedIn(Guid tenant, Guid facility, Action<IEndpointRouteBuilder>? extra) : IStartupFilter
    {
        public const string UserHeader = "X-Test-User";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ctx.Request.Headers[UserHeader].ToString()), new Claim("tenant_id", tenant.ToString()),
                     new Claim("facility_id", facility.ToString())], "synthetic"));
                return nxt(ctx);
            });
            next(app);
            if (extra is null) return;
            app.UseRouting();
            app.UseEndpoints(extra);
        };
    }

    [Fact]
    public void EverySignedInEndpointIsAuditedOrReviewedAsFreeOfPatientData()
    {
        using var factory = Host();
        Assert.Empty(Unclassified(factory));
    }

    [Fact]
    public async Task EveryAuditedEndpointWritesExactlyOneAuditRowPerRequest()
    {
        using var factory = Host();
        var calls = await AuditRowsPerRequest(factory);
        Assert.NotEmpty(calls);
        Assert.All(calls, e =>
        {
            Assert.All(e.Value, c => Assert.True(c.Rows == 1, $"{e.Key} as {c.Role}: {c.Rows} audit rows (HTTP {c.Status})"));
            // One outcome rule for every path: the row says what the caller was told (403 Denied, 404 NotFound, 400/409 Rejected).
            Assert.All(e.Value, c => Assert.True(c.Outcomes.Count != 1 || c.Outcomes[0] == AuditedEndpoint.OutcomeFor(c.Status),
                $"{e.Key} as {c.Role}: HTTP {c.Status} audited as {string.Join(",", c.Outcomes)}"));
            // Without this a capability-guarded endpoint could pass on refused requests alone and its allowed path never be counted.
            Assert.True(e.Value.Exists(c => c.Allowed), $"{e.Key}: every role was refused, so the allowed path was not checked");
        });
        // Writes whose use case audits in its own transaction: the allowed path ran for some role and left exactly that one event.
        foreach (var write in WritesWithSampleBodies)
            Assert.Contains(calls[write], c => c.Outcomes.SequenceEqual([Medvya.Audit.Domain.AuditOutcome.Allowed]) && c.Status == 200);
    }

    [Fact]
    public async Task CheckerCatchesUnauditedAndUncountedPatientEndpoints()
    {
        // Not marked public, so each counts as signed-in. No RequireAuthorization: these route after the host's authorization middleware, which would refuse them.
        using var factory = Host(e =>
        {
            e.MapGet("/test/patients/{patientId}", () => "synthetic").Audited("patient.read", "patient", patientRouteKey: "patientId");
            e.MapGet("/test/unaudited/{patientId}", () => "synthetic");
            // Tagged as audited but the filter is bypassed: the count must catch it.
            e.MapGet("/test/tagged-only/{patientId}", () => "synthetic").WithMetadata(new AuditedEndpointMetadata("patient.read", "patient", "treatment", "patientId"));
        });
        _ = factory.CreateClient(); // builds the pipeline so the extra endpoints register

        Assert.Equal(["GET /test/unaudited/{patientId}"], Unclassified(factory));
        var calls = await AuditRowsPerRequest(factory);
        Assert.All(calls["GET /test/patients/{patientId}"], c => Assert.Equal((1, 200), (c.Rows, c.Status)));
        Assert.All(calls["GET /test/tagged-only/{patientId}"], c => Assert.Equal((0, 200), (c.Rows, c.Status)));
    }
}
