namespace Medvya.BuildingBlocks.Domain;

/// <summary>The tenant (clinic organisation) of the current request. Null means "no tenant": the database returns no tenant rows.</summary>
public interface ICurrentTenant
{
    Guid? TenantId { get; }
}
