using Azure.Identity;
using Azure.Storage.Blobs;
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
            .AddDbContext<AuditDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IAuditLog, AuditLog>()
            .AddSingleton<IPlatformAuditLog>(new PlatformAuditLog(connectionString))
            .AddScoped<IAuditTrail, AuditTrail>();
    }

    /// <summary>
    /// Signed daily digests of the ledger chains (MDV-P0-036). Storage is Storage:BlobEndpoint with the managed identity (Azure, MDV-P0-042)
    /// or Storage:ConnectionString (local Azurite). The key is Audit:DigestKey (base64; a Key Vault reference in Azure, never committed; locally
    /// the environment variable Audit__DigestKey). Storage without a key is a startup error, so digests are never signed with a default.
    /// <paramref name="requireStorage"/> (every environment but Development) makes missing storage a startup error too, so a deployment
    /// cannot run with no digests and no verification.
    /// </summary>
    public static IServiceCollection AddLedgerDigests(this IServiceCollection services, string connectionString, IConfiguration configuration, bool requireStorage)
    {
        BlobDigestStore? store = configuration["Storage:BlobEndpoint"] is { Length: > 0 } endpoint
            ? new(new BlobContainerClient(new Uri(new Uri(endpoint), BlobDigestStore.Container), new DefaultAzureCredential()), createContainer: false)
            : configuration["Storage:ConnectionString"] is { Length: > 0 } storage ? new(new BlobContainerClient(storage, BlobDigestStore.Container), createContainer: true) : null;
        if (store is null)
            return requireStorage ? throw new InvalidOperationException("Ledger digests need Storage:BlobEndpoint or Storage:ConnectionString.") : services;
        var key = configuration["Audit:DigestKey"] is { Length: > 0 } k ? Convert.FromBase64String(k) : throw new InvalidOperationException("Audit:DigestKey is required when blob storage is configured.");
        services.TryAddSingleton(TimeProvider.System);
        return services.AddSingleton<IDigestStore>(store)
            .AddSingleton(sp => new LedgerDigests(connectionString, key, sp.GetRequiredService<IDigestStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LedgerDigests>>()))
            .AddScoped<LedgerJobs>();
    }
}
