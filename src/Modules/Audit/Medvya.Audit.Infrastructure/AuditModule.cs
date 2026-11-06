using Medvya.Audit.Application;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Audit.Infrastructure;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System); // the audit window needs a clock; hosts and tests that register their own win
        return services
            .AddSingleton<AuditWindow>()
            .AddScoped<TenantSessionInterceptor>()
            .AddDbContext<AuditDbContext>((sp, o) => o.UseNpgsql(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IAuditLog, AuditLog>()
            .AddScoped<IAuditTrail, AuditTrail>();
    }

    /// <summary>
    /// Signed daily digests of the ledger chains (MDV-P0-036). Needs Storage:ConnectionString and Audit:DigestKey (base64; Key Vault in the cloud,
    /// a local key in appsettings.Development.json). Storage set without a key is a startup error: digests are never signed with a default.
    /// </summary>
    public static IServiceCollection AddLedgerDigests(this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        if (configuration["Storage:ConnectionString"] is not { Length: > 0 } storage) return services;
        var key = configuration["Audit:DigestKey"] is { Length: > 0 } k ? Convert.FromBase64String(k) : throw new InvalidOperationException("Audit:DigestKey is required when blob storage is configured.");
        services.TryAddSingleton(TimeProvider.System);
        return services.AddSingleton<IDigestStore>(new BlobDigestStore(storage))
            .AddSingleton(sp => new LedgerDigests(connectionString, key, sp.GetRequiredService<IDigestStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LedgerDigests>>()))
            .AddScoped<LedgerJobs>();
    }
}
