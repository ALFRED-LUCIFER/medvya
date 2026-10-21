using System.Threading.RateLimiting;
using Host.Api.Auth;
using Host.Api.Context;
using Host.Api.Middleware;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Identity.Infrastructure;
using Medvya.Scheduling.Api;
using Medvya.Scheduling.Infrastructure;
using Medvya.Tenancy.Infrastructure;
using Host.Api.Observability;

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
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
// Without a database, audited and flag-gated endpoints cannot resolve IAuditLog / IFeatureFlags and fail closed (500).
if (builder.Configuration.GetConnectionString("Medvya") is { } sql) builder.Services.AddAuditModule(sql).AddTenancyModule(sql).AddIdentityModule(sql).AddSchedulingModule(sql);
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
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders();
app.UseRateLimiter();
app.UseStaffAuthentication();

// Deploy smoke tests (MDV-P0-007) read the version from here.
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }))
    .AllowAnonymous(); // public: deploy smoke tests
app.MapStaffSession();
app.MapSchedulingEndpoints();
if (app.Environment.IsDevelopment() && app.Configuration["Authentication:Oidc:Authority"]?.EndsWith(DevIdentityProvider.Path, StringComparison.Ordinal) == true)
    app.MapDevIdentityProvider();

app.Run();
