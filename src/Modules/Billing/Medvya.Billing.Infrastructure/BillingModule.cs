using Medvya.Billing.Application;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Billing.Infrastructure;

/// <summary>shortcut: not registered by the host yet; the first caller is invoice issuing (MDV-P4-004), which adds it to Program.cs.</summary>
public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        return services
            .AddDbContext<BillingDbContext>((sp, o) => o.UseSqlServer(connectionString).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IDocumentNumbering, DocumentNumbering>();
    }
}
