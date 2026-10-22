namespace Medvya.BuildingBlocks.Domain;

/// <summary>The facility the signed-in staff member is working at (chosen after sign-in). Null means "no facility": access checks fail.</summary>
public interface ICurrentFacility
{
    Guid? FacilityId { get; }
}
