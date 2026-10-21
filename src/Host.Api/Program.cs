using System.Threading.RateLimiting;
using Host.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    // Files go straight to Blob via scoped SAS, so API bodies stay small; raise per endpoint if one needs more.
    k.Limits.MaxRequestBodySize = 1_048_576;
});

builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

var permitsPerMinute = builder.Configuration.GetValue("RateLimiting:PermitsPerMinute", 300);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per client IP across the whole API; sign-in/OTP get a stricter policy with their endpoints (MDV-P0-011).
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSecurityHeaders();
app.UseRateLimiter();

// Deploy smoke tests (MDV-P0-007) read the version from here.
app.MapGet("/health", () => Results.Ok(new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }));

app.Run();
