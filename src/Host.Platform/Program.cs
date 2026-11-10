using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Host.Platform.Auth;
using Host.Platform.Context;
using Medvya.Audit.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.BuildingBlocks.Security;
using Medvya.Platform.Api;
using Medvya.Platform.Application;
using Medvya.Platform.Infrastructure;
using Medvya.Platform.Infrastructure.Persistence;
using Medvya.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;

// The platform console host (MDV-P5-040, ADR-014): a separate app for the Medvya team. It reads only the control plane and signs in to the
// database as medvya_platform, which has no right on any clinic schema.
// shortcut: no telemetry exporter, forwarded-headers or data-protection key store yet; add them with the console's Azure app and
// infrastructure (the Entra app registration and resources are people-and-Azure work, see docs/05-delivery/session-handoff.md).
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = 65_536; });

builder.AddConsoleAuthentication();
builder.Services.AddAuthorizationBuilder().AddPlatformPolicies();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPlatformCaller, PlatformCaller>();
// Without a database nothing resolves the console's services, so every endpoint fails closed.
if (builder.Configuration.GetConnectionString("Platform") is { } sql)
    builder.Services.AddPlatformModule(sql, builder.Configuration.GetSection("Platform").Get<PlatformOptions>() ?? new());
// The organisation review (MDV-P1-064) signs in as medvya_review: the onboarding tables and an append to the clinic audit log, nothing else
// (infra/postgres/table-rights.sql). Without it the review endpoints fail closed like the rest.
if (builder.Configuration.GetConnectionString("Review") is { } review)
{
    builder.Services.AddScoped<ReviewContext>()
        .AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<ReviewContext>())
        .AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ReviewContext>())
        .AddScoped<ILinkTenant>(sp => sp.GetRequiredService<ReviewContext>())
        .AddEmailSender(builder.Configuration, () => new Azure.Identity.ManagedIdentityCredential(Azure.Identity.ManagedIdentityId.SystemAssigned))
        .AddAuditModule(review)
        .AddOrganisationReview(review, builder.Configuration["Storage:ConnectionString"], builder.Configuration["Storage:BlobEndpoint"]);
}
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitsPerMinute", 300), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(ConsoleAuthentication.SignInRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:SignInPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders();
app.UseRateLimiter();
// The built console (web/platform-admin) when the deploy copies it to wwwroot; client routes reload into its shell.
app.UseStaticFiles();
app.UseConsoleAuthentication();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous(); // public: deploy smoke tests
app.MapConsoleSession();
app.MapPlatformConsole();
app.MapOrganisationReview();
app.MapFallbackToFile("{*path:nonfile}", "index.html").AllowAnonymous();
if (app.Environment.IsDevelopment() && app.Configuration["Authentication:Oidc:Authority"]?.EndsWith(DevIdentityProvider.Path, StringComparison.Ordinal) == true)
{
    var boot = app.Configuration.GetSection("Platform:BootstrapOwner").Get<BootstrapOwner>();
    // The dev provider stands in for the company directory: everyone ever bound to it (a removed member's company account still exists,
    // so the console must refuse it), and the first owner while the team is empty.
    app.MapDevIdentityProvider(DevCompanyIssuer, async http =>
    {
        var db = http.RequestServices.GetRequiredService<PlatformDbContext>();
        var members = await db.Members.AsNoTracking().Where(m => m.Issuer == DevCompanyIssuer && m.Subject != null)
            .OrderBy(m => m.DisplayName).Select(m => new DevAccount(m.Subject!, m.DisplayName)).ToListAsync(http.RequestAborted);
        return boot is { Subject: { } s } && members.Count == 0 ? [new DevAccount(s, boot.DisplayName)] : members;
    });
}

app.Run();

public partial class Program
{
    /// <summary>The development provider's issuer, standing in for the company directory (appsettings.Development.json).</summary>
    public const string DevCompanyIssuer = "medvya-dev-company-idp";
}
