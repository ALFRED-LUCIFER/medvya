namespace Medvya.BuildingBlocks.Domain;

/// <summary>The signed-in user of the current request (opaque id only). Null before sign-in exists (MDV-P0-011).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
