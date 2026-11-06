namespace Medvya.BuildingBlocks.Domain;

/// <summary>Scopes a fresh DI scope to a tenant named by a tenant-bound link, job or display token instead of the session (see LinkTenantScope).</summary>
public interface ILinkTenant
{
    void UseLinkTenant(Guid tenantId);
}
