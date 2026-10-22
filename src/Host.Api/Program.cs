using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Host.Api.Auth;
using Host.Api.Context;
using Host.Api.Invitations;
using Host.Api.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
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

var builder = WebApplication.CreateBuilder(args);
builder.AddMedvyaTelemetry();

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    // Files go straight to Blob via scoped SAS, so API bodies stay small; raise per endpoint if one needs more.
    k.Limits.MaxRequestBodySize = 1_048_576;
});

builder.AddStaffAuthentication();
builder.Services.AddAuthorizationBuilder().AddStaffPolicies().AddStaffAdminPolicy();
// Enums travel as names ("Receptionist"), never as numbers that shift when an enum changes.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Email:Smtp")).AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<InvitationOptions>(builder.Configuration.GetSection("Invitations"));
builder.AddPublicSite();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentFacility>(sp => sp.GetRequiredService<RequestContext>());
// Without a database, audited and flag-gated endpoints cannot resolve IAuditLog / IFeatureFlags and fail closed (500), and no
// capability handler exists, so capability-guarded endpoints answer 403.
if (builder.Configuration.GetConnectionString("Medvya") is { } sql)
{
    builder.Services.AddAuditModule(sql).AddDeniedAccessAudit().AddTenancyModule(sql).AddIdentityModule(sql).AddPatientsModule(sql).AddSchedulingModule(sql)
        .AddLeadsModule(sql, builder.Configuration.GetSection("Leads:Email").Get<LeadsEmailOptions>() ?? new()).AddCapabilityPolicies();
    // shortcut: only Development has a column master key (the same local file as `pnpm dev:db`); the Azure Key Vault provider
    // arrives with MDV-P0-006, and until then encrypted identifiers cannot be read anywhere else (fail closed).
    if (builder.Environment.IsDevelopment())
        LocalKeyStoreProvider.Register(() => LocalKeyStoreProvider.LoadOrCreate(builder.Configuration["ColumnEncryption:LocalKeyFile"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Medvya", "local-column-master-key.pem")));
}
builder.Services.Configure<SchedulingOptions>(builder.Configuration.GetSection("Scheduling"));
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

// Behind the App Service front end the client is in X-Forwarded-For. Only listed proxies are believed, so a client cannot
// pick its own address for the per-IP limits or the audit trail; with none listed (the default, as in Development) the header
// is ignored. shortcut: no proxy is listed until the App Service network is known; set ForwardedHeaders:KnownProxies (MDV-P0-007).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    var proxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var proxy in proxies) o.KnownProxies.Add(IPAddress.Parse(proxy));
    // With both lists empty the middleware would believe any sender, so no listed proxy means no forwarded headers at all.
    o.ForwardedHeaders = proxies.Length > 0 ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto : ForwardedHeaders.None;
});

var permitsPerMinute = builder.Configuration.GetValue("RateLimiting:PermitsPerMinute", 300);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per client IP across the whole API.
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // Starting sign-in is cheap to abuse; codes and lockout are enforced by the identity provider.
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

var app = builder.Build();

app.UseForwardedHeaders(); // first, so every later step sees the client's address
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders();
app.UseRateLimiter();
app.RunPublicSiteBeforeAuthentication(); // public pages never read the session cookie
app.UseStaffAuthentication();

// Deploy smoke tests (MDV-P0-007) read the version from here.
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }))
    .AllowAnonymous(); // public: deploy smoke tests
app.MapStaffSession();
app.MapSchedulingEndpoints();
app.MapPublicSite();
app.MapInvitations();
app.MapPatientEndpoints();
if (app.Environment.IsDevelopment() && app.Configuration["Authentication:Oidc:Authority"]?.EndsWith(DevIdentityProvider.Path, StringComparison.Ordinal) == true)
    app.MapDevIdentityProvider();

app.Run();
