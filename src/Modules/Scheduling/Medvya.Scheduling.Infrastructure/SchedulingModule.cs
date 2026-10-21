using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Scheduling.Infrastructure;

public static class SchedulingModule
{
    /// <summary>Needs the Tenancy module (facilities, memberships) and the Identity module (doctor names).</summary>
    public static IServiceCollection AddSchedulingModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<SchedulingDbContext>((sp, o) => o.UseSqlServer(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IScheduleService, ScheduleService>();
    }
}
