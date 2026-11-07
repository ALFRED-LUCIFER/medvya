using Hangfire;
using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Host.Api.Auth;
using Host.Api.Context;
using Host.Api.Middleware;
using Medvya.Identity.Api;
using Medvya.Encounters.Api;
using Medvya.Encounters.Infrastructure;
using Medvya.Notifications.Api;
using Medvya.Notifications.Application;
using Medvya.Notifications.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Medvya.Audit.Api;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Identity.Infrastructure;
using Medvya.Patients.Api;
using Medvya.Patients.Infrastructure;
using Medvya.Scheduling.Api;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Infrastructure;
using Medvya.Leads.Infrastructure;
using Medvya.Tenancy.Api;
using Medvya.Tenancy.Infrastructure;
using Host.Api.Observability;
using Host.Api.PublicSite;
using Medvya.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddMedvyaTelemetry();
DevClock.AddIfDevelopment(builder); // before the modules: they add the system clock only when none is registered

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    // Files go straight to Blob via scoped SAS, so API bodies stay small; raise per endpoint if one needs more.
    k.Limits.MaxRequestBodySize = 1_048_576;
});

builder.AddStaffAuthentication();
builder.AddNativeAuthentication(); // the staff app's bearer tokens, on the same server-side sessions (MDV-P5-033)
// Waiting-room TVs sign in with a display token, never a staff session (MDV-P1-031).
builder.Services.AddAuthentication().AddQueueDisplay();
builder.Services.AddAuthorizationBuilder().AddStaffPolicies().AddStaffAdminPolicy().AddQueueDisplayPolicy().AddOperationsPolicies(builder.Configuration);
// Enums travel as names ("Receptionist"), never as numbers that shift when an enum changes.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEmailSender(builder.Configuration, () => new Azure.Identity.ManagedIdentityCredential(Azure.Identity.ManagedIdentityId.SystemAssigned));
builder.Services.Configure<InvitationOptions>(builder.Configuration.GetSection("Invitations"));
builder.AddPublicSite();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentFacility>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ILinkTenant>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<IRequestOrigin>(sp => sp.GetRequiredService<RequestContext>());
// Without a database, audited and flag-gated endpoints cannot resolve IAuditLog / IFeatureFlags and fail closed (500), and no
// capability handler exists, so capability-guarded endpoints answer 403.
if (builder.Configuration.GetConnectionString("Medvya") is { } sql)
{
    builder.Services.AddAuditModule(sql).AddDeniedAccessAudit().AddTenancyModule(sql, builder.Configuration["Storage:ConnectionString"]).AddIdentityModule(sql).AddPatientsModule(sql).AddSchedulingModule(sql).AddEncountersModule(sql)
        .AddNotificationsModule(sql, smsSink: builder.Environment.IsDevelopment() && builder.Configuration["Notifications:Sms:Provider"] == "Sink")
        .AddScoped<IReminderConsent, PatientReminderConsent>() // after the module: replaces its deny-all default (MDV-P1-019)
        .AddLeadsModule(sql, builder.Configuration.GetSection("Leads:Email").Get<LeadsEmailOptions>() ?? new()).AddCapabilityPolicies();
}
// Ledger digests (MDV-P0-036). Development signs them only when a key is set outside the repository (environment Audit__DigestKey);
// everywhere else a missing key or digest storage is a startup error.
if (builder.Configuration.GetConnectionString("Medvya") is { } ledgerSql && (!builder.Environment.IsDevelopment() || builder.Configuration["Audit:DigestKey"] is { Length: > 0 }))
    builder.Services.AddLedgerDigests(ledgerSql, builder.Configuration, requireStorage: !builder.Environment.IsDevelopment());
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
// Reminder consent comes from the patient's consent receipts. Local runs may switch the check off explicitly; never outside Development.
if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Notifications:DevAllowAllConsent", false))
    builder.Services.AddSingleton<IReminderConsent, DevAllowAllConsent>();
// The terms gate (MDV-P1-019) may be switched off for local runs only; anywhere else that setting is a startup error, never a silent bypass.
if (!builder.Environment.IsDevelopment() && !builder.Configuration.GetValue("Terms:RequireAcceptance", true))
    throw new InvalidOperationException("Terms:RequireAcceptance=false is allowed in Development only.");
builder.AddJobs(builder.Configuration.GetConnectionString("Medvya"));
builder.Services.Configure<SchedulingOptions>(builder.Configuration.GetSection("Scheduling"));
// Live board (MDV-P1-029): every hub call runs as the connection's signed-in user, with its session checked again.
builder.Services.AddSchedulingRealtime().AddHubOptions<BoardHub>(o => o.AddFilter<HubRequestContext>());
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

// The client address for the per-IP limits and the audit trail. A forged one would dodge limits and falsify the trail, so
// X-Forwarded-For is believed only where we know a proxy is in front (MDV-P0-007):
// - On App Service (it sets WEBSITE_INSTANCE_ID; a client cannot): every request arrives through the platform front end, whose
//   addresses are not fixed, and the app cannot be reached around it. Only the last entry is read (ForwardLimit 1), the one the
//   front end added; anything a client wrote into the header itself sits further left and is ignored.
// - Elsewhere: only from proxies listed in ForwardedHeaders:KnownProxies; none listed (Development) means the header is ignored.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    var proxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    var appService = !string.IsNullOrEmpty(builder.Configuration["WEBSITE_INSTANCE_ID"]);
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear(); // both lists empty = every peer is believed, which only App Service's own front end can be
    foreach (var proxy in proxies) o.KnownProxies.Add(IPAddress.Parse(proxy));
    o.ForwardLimit = 1;
    // The protocol is always believed: App Service ends TLS in front of the app, so without it sign-in sends Entra an http redirect
    // address that is not registered. A forged protocol can only change generated links.
    o.ForwardedHeaders = appService || proxies.Length > 0 ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.XForwardedProto;
});

var permitsPerMinute = builder.Configuration.GetValue("RateLimiting:PermitsPerMinute", 300);
builder.Services.AddOpenApi(); // document only, exported at build time; no endpoint serves it
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per client IP across the whole API.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // Starting sign-in is cheap to abuse; codes and lockout are enforced by the identity provider.
    o.AddQueueDisplayPairingLimit(builder.Configuration.GetValue("RateLimiting:DisplayPairingPerMinute", 20));
    o.AddPolicy(StaffAuthentication.SignInRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:SignInPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
});
// Patient search per signed-in user: a desk searches a few times a minute; bulk enumeration is refused. Enforced after sign-in
// (an endpoint filter), because the middleware above runs before authentication and would key every caller as anonymous.
builder.Services.AddUserRateLimit(PatientEndpoints.SearchRateLimit, builder.Configuration.GetValue("RateLimiting:PatientSearchPerMinute", 30));
// Opening records (and revealing their numbers): a desk opens a few a minute; bulk reading is refused. Per user, after sign-in.
builder.Services.AddUserRateLimit(PatientEndpoints.RecordRateLimit, builder.Configuration.GetValue("RateLimiting:PatientRecordsPerMinute", 30));
// The duplicate check runs (debounced) while the desk types: a few calls per registration. Per user, after sign-in, likewise.
builder.Services.AddUserRateLimit(PatientEndpoints.DuplicateCheckRateLimit, builder.Configuration.GetValue("RateLimiting:DuplicateChecksPerMinute", 60));
// Joining the live board: once per page load and per reconnect; a client looping on it is refused. Per user, after sign-in.
builder.Services.AddUserRateLimit(BoardHub.WatchRateLimit, builder.Configuration.GetValue("RateLimiting:BoardWatchPerMinute", 30));

var app = builder.Build();

app.UseForwardedHeaders(); // first, so every later step sees the client's address
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders(app.Configuration["Storage:ImageOrigin"]);
app.UseRateLimiter();
app.UseWebApp(); // static files only; reads no session cookie
app.RunPublicSiteBeforeAuthentication(); // public pages never read the session cookie
app.UseStaffAuthentication();

// Deploy smoke tests (MDV-P0-007) read the version from here: the tag the deploy published with, plus the commit.
var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
app.MapGet("/health", () => Results.Ok(new { status = "ok", version }))
    .AllowAnonymous(); // public: deploy smoke tests
app.MapStaffSession();
app.MapNativeSignIn();
app.MapSchedulingEndpoints();
app.MapNotifications();
app.UseJobs();
if (app.Services.GetService<IRecurringJobManager>() is { } recurring && app.Services.GetService<Medvya.Audit.Infrastructure.LedgerDigests>() is not null)
{
    recurring.AddOrUpdate<Medvya.Audit.Infrastructure.LedgerJobs>(Medvya.Audit.Infrastructure.LedgerJobs.PublishJobId, j => j.PublishAsync(CancellationToken.None), Cron.Daily(1));
    recurring.AddOrUpdate<Medvya.Audit.Infrastructure.LedgerJobs>(Medvya.Audit.Infrastructure.LedgerJobs.VerifyJobId, j => j.VerifyAsync(CancellationToken.None), Cron.Daily(2));
}
app.MapPublicSite();
app.MapInvitations();
app.MapOnboarding();
app.MapPracticeSettings();
app.MapDoctors();
app.MapStaff();
app.MapAccessLog();
app.MapQueueDisplaySession();
app.MapPatientEndpoints();
app.MapAccessRequestEndpoints();
app.MapEncounterEndpoints();
if (app.Environment.IsDevelopment() && app.Configuration["Authentication:Oidc:Authority"]?.EndsWith(DevIdentityProvider.Path, StringComparison.Ordinal) == true)
    app.MapDevIdentityProvider(Medvya.Identity.Domain.IdentityProviders.Development, async http =>
        await http.RequestServices.GetRequiredService<Medvya.Identity.Infrastructure.Persistence.IdentityDbContext>().Users.AsNoTracking()
            .Where(u => u.Issuer == Medvya.Identity.Domain.IdentityProviders.Development).OrderBy(u => u.DisplayName)
            .Select(u => new DevAccount(u.Subject, u.DisplayName)).ToListAsync(http.RequestAborted));

app.Run();
