using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Tenancy.Infrastructure;

public static class TenancyModule
{
    /// <summary>Needs the Audit module too: flag changes are audited.</summary>
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        return services
            .AddDbContext<TenancyDbContext>((sp, o) => o.UseSqlServer(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IFeatureFlags, FeatureFlags>()
            .AddScoped<IFacilityDirectory, FacilityDirectory>()
            .AddScoped<IMembershipDirectory>(sp => new MembershipDirectory(connectionString, sp.GetRequiredService<ICurrentUser>()));
    }
}
