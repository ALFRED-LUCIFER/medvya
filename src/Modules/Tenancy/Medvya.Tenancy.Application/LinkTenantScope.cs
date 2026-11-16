using Medvya.BuildingBlocks.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Tenancy.Application;

public static class LinkTenantScope
{
    /// <summary>A DI scope whose services (DbContexts, audit, RLS session context) run in <paramref name="tenantId"/>.</summary>
    public static AsyncServiceScope EnterLinkTenant(this IServiceProvider services, Guid tenantId)
    {
        var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ILinkTenant>().UseLinkTenant(tenantId);
        return scope;
    }
}
