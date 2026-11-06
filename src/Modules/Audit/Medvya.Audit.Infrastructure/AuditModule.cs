using Medvya.Audit.Application;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
}
