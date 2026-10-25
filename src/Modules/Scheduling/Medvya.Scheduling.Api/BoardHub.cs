using System.Threading.RateLimiting;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Scheduling.Api;

/// <summary>What a board receives when a visit changes: ids, state and time only. Boards then refetch the audited board endpoint.</summary>
public sealed record BoardChanged(Guid AppointmentId, string State, DateTimeOffset ChangedAt);

/// <summary>
/// Real-time reception board (MDV-P1-029) at <see cref="Path"/>. Connecting needs board access ("Check-in &amp; queue", read or
/// full) at the session's facility, checked by the endpoint's capability policy; a refusal is audited Denied. Every call is then
/// checked again: the session must still be live (host hub filter) and <see cref="Watch"/> joins only the session's own facility
/// after the same access check as the board itself, audited Allowed or Denied. The group name carries the tenant from the
/// session, never from the client, so a connection cannot reach another tenant's or facility's boards. The hub has no write
/// methods: calling in and moving visits go through the audited HTTP endpoints and the visit state machine.
/// shortcut: a session revoked while connected keeps receiving id/state pings until its next call or reconnect (its refetches
/// are refused at once); end hub connections on revocation when sessions publish a revocation event (MDV-P0-012).
/// </summary>
public sealed class BoardHub(
    IFrontDeskService frontDesk, IAuditLog audit, ICurrentTenant tenant, ICurrentUser user, ICurrentFacility session,
    [FromKeyedServices(BoardHub.WatchRateLimit)] PartitionedRateLimiter<string> limiter) : Hub
{
    public const string Path = "/v1/hubs/board";

    /// <summary>Per-user limit on <see cref="Watch"/> (registered by the host with the per-user limits).</summary>
    public const string WatchRateLimit = "board-watch";

    public const string ConnectAction = "front_desk.board.connect", WatchAction = "front_desk.board.watch";

    /// <summary>The only refusal a caller is told; it never says why.</summary>
    public const string Refused = "Not allowed at this facility.";

    /// <summary>Starts receiving <see cref="BoardChanged"/> for the facility the session is scoped to.</summary>
    public async Task Watch(Guid facilityId)
    {
        // Without a tenant there is nowhere to write an audit row: refused unaudited, as the HTTP endpoints do.
        if (tenant.TenantId is null || user.UserId is not { } me) throw new HubException(Refused);
        using var lease = await limiter.AcquireAsync(me.ToString(), 1, Context.ConnectionAborted);
        if (!lease.IsAcquired) throw new HubException("Too many requests."); // nothing was read, so not audited (as the HTTP per-user limits)

        string group;
        try
        {
            // Only the facility this session is scoped to (facility picker, MDV-P0-012), and only with board access there now.
            if (facilityId != session.FacilityId) throw new SchedulingAccessDeniedException();
            group = await frontDesk.WatchGroupAsync(facilityId, Context.ConnectionAborted);
        }
        catch (SchedulingAccessDeniedException)
        {
            await AuditAsync(AuditOutcome.Denied);
            throw new HubException(Refused);
        }
        await AuditAsync(AuditOutcome.Allowed); // before joining: no audit row, no updates (fail closed)
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    Task AuditAsync(AuditOutcome outcome) => audit.WriteAsync(new AuditEvent
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.TenantId!.Value, ActorUserId = user.UserId, Action = WatchAction,
        ResourceType = SchedulingAudit.AppointmentResource, Purpose = SchedulingAudit.Treatment, Outcome = outcome, OccurredAt = DateTimeOffset.UtcNow,
    }, Context.ConnectionAborted);
}

/// <summary>Sends board changes to the facility's group in this tenant.</summary>
// shortcut: in-process SignalR reaches only connections on this instance; add Azure SignalR Service (in country) before
// running more than one API instance (MDV-P0-007).
public sealed class SignalRBoardNotifier(IHubContext<BoardHub> hub) : IBoardNotifier
{
    public Task ChangedAsync(BoardChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        return hub.Clients.Group(BoardGroups.Of(change.TenantId, change.FacilityId))
            .SendAsync(nameof(BoardChanged), new BoardChanged(change.AppointmentId, change.State.ToString(), change.ChangedAt), cancellationToken);
    }
}

public static class SchedulingRealtime
{
    /// <summary>SignalR for the reception board; replaces the module's no-op notifier.</summary>
    public static ISignalRServerBuilder AddSchedulingRealtime(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IBoardNotifier, SignalRBoardNotifier>());
        return services.AddSignalR();
    }
}
