namespace Medvya.BuildingBlocks.Domain;

/// <summary>The signed-in user of the current request (opaque id only). Null before sign-in exists (MDV-P0-011).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>When the user last re-entered their credentials in this session (step-up for signing); null if never, or no browser session.</summary>
    DateTimeOffset? ReauthenticatedAt => null;
}
