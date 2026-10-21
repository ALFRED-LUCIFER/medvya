using Medvya.Audit.Application;
using Medvya.Audit.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Audit.Infrastructure;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, string connectionString) =>
        services
            .AddScoped<TenantSessionInterceptor>()
            .AddDbContext<AuditDbContext>((sp, o) => o.UseSqlServer(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IAuditLog, AuditLog>();
}
