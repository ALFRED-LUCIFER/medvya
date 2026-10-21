using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Host.Api.Auth;
using Host.Api.Context;
using Host.Api.Middleware;
using Medvya.Audit.Api;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Infrastructure;
using Medvya.Patients.Api;
using Medvya.Patients.Infrastructure;
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
builder.Services.AddAuthorization();
builder.Services.AddPublicSite();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentFacility>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Without a database, audited and flag-gated endpoints cannot resolve IAuditLog / IFeatureFlags and fail closed (500), and no
// capability handler exists, so capability-guarded endpoints answer 403.
if (builder.Configuration.GetConnectionString("Medvya") is { } sql)
{
    builder.Services.AddAuditModule(sql).AddDeniedAccessAudit().AddTenancyModule(sql).AddIdentityModule(sql).AddPatientsModule(sql).AddCapabilityPolicies();
    // shortcut: only Development has a column master key (the same local file as `pnpm dev:db`); the Azure Key Vault provider
    // arrives with MDV-P0-006, and until then encrypted identifiers cannot be read anywhere else (fail closed).
    if (builder.Environment.IsDevelopment())
        LocalKeyStoreProvider.Register(() => LocalKeyStoreProvider.LoadOrCreate(builder.Configuration["ColumnEncryption:LocalKeyFile"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Medvya", "local-column-master-key.pem")));
}
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

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
    // Patient search per signed-in user: a desk searches a few times a minute; bulk enumeration is refused.
    o.AddPolicy(PatientEndpoints.SearchRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:PatientSearchPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
    // The duplicate check runs (debounced) while the desk types: a few calls per registration.
    o.AddPolicy(PatientEndpoints.DuplicateCheckRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:DuplicateChecksPerMinute", 60), Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders();
app.UseRateLimiter();
app.RunPublicSiteBeforeAuthentication(); // public pages never read the session cookie
app.UseStaffAuthentication();

// Deploy smoke tests (MDV-P0-007) read the version from here.
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }))
    .AllowAnonymous(); // public: deploy smoke tests
app.MapStaffSession();
app.MapPublicSite();
app.MapPatientEndpoints();
if (app.Environment.IsDevelopment() && app.Configuration["Authentication:Oidc:Authority"]?.EndsWith(DevIdentityProvider.Path, StringComparison.Ordinal) == true)
    app.MapDevIdentityProvider();

app.Run();
