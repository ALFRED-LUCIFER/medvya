namespace Medvya.Scheduling.Application;

/// <summary>
/// What a waiting-room TV shows (P-05, MDV-P1-031): the tokens being called with their room and doctor, and the next tokens
/// with an estimated wait. Tokens, rooms, doctor display names and minutes only: never a patient's name, initials, MRN, date
/// of birth or reason (a contract test pins these exact fields).
/// </summary>
public sealed record QueueTv(IReadOnlyList<QueueTvNow> Now, IReadOnlyList<QueueTvNext> Next);

/// <summary>A token called in: where to go and to whom. Room is null when the doctor's session has none.</summary>
public sealed record QueueTvNow(string Token, string? Room, string Doctor);

/// <summary>A waiting token and about how many minutes until it is called.</summary>
public sealed record QueueTvNext(string Token, int WaitMinutes);

/// <summary>Adds a TV for the facility; Name says where it hangs ("Waiting room TV").</summary>
public sealed class CreateDisplay
{
    public Guid FacilityId { get; init; }
    public string? Name { get; init; }
}

public sealed record DisplayView(Guid Id, Guid FacilityId, string Name, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

/// <summary>The new display and its token, returned once: only a hash is stored, so a lost token means a new display.</summary>
public sealed record CreatedDisplay(DisplayView Display, string Token);

/// <summary>A live display: the TV it is and the one facility whose queue it may show.</summary>
public sealed record DisplaySession(Guid DisplayId, Guid FacilityId);

/// <summary>Waiting-room TVs and their queue (MDV-P1-031).</summary>
public interface IQueueService
{
    /// <summary>The queue TV of the signed-in display's facility; the facility comes from the display, never the request.</summary>
    Task<QueueTv> TvAsync(CancellationToken cancellationToken = default);

    /// <exception cref="SchedulingAccessDeniedException">Not allowed to manage the facility's devices.</exception>
    Task<IReadOnlyList<DisplayView>> ListDisplaysAsync(Guid facilityId, CancellationToken cancellationToken = default);

    /// <exception cref="SchedulingAccessDeniedException">Not allowed to manage the facility's devices.</exception>
    Task<CreatedDisplay> CreateDisplayAsync(CreateDisplay command, CancellationToken cancellationToken = default);

    /// <summary>Ends the display: its next request is refused. Revoking twice is a no-op.</summary>
    /// <exception cref="DisplayNotFoundException">No display with this id in the tenant.</exception>
    Task RevokeDisplayAsync(Guid displayId, CancellationToken cancellationToken = default);

    /// <summary>The live display with this token hash in the current tenant, or null when it is unknown or revoked.</summary>
    Task<DisplaySession?> FindDisplayAsync(byte[] tokenHash, CancellationToken cancellationToken = default);

    /// <summary>As <see cref="FindDisplayAsync"/>, and audits the pairing of a TV with the token (the display is the actor).</summary>
    Task<DisplaySession?> PairDisplayAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
}

/// <summary>Real-time groups of queue TVs: opaque ids only, the tenant part of the name, so a group never spans tenants.</summary>
public static class QueueGroups
{
    public static string Of(Guid tenantId, Guid facilityId) => $"queue:{tenantId:N}:{facilityId:N}";
}
