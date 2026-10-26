using System.Security.Claims;
using Medvya.Scheduling.Application;
using Microsoft.AspNetCore.SignalR;

namespace Medvya.Scheduling.Api;

/// <summary>
/// How a waiting-room TV signs in (MDV-P1-031): no staff session. Staff who may configure the facility create a display and
/// get a link `/queue/tv#{token}` once; the token is in the fragment, so it never reaches a server log, a URL or a Referer.
/// The TV posts it to `/v1/queue/tv/session`, which checks it and sets an HttpOnly, Secure, SameSite=Strict cookie limited to
/// `/v1/queue`. The host's display scheme checks that cookie on every request (revoked = refused) and gives a principal with
/// the display's tenant and facility only. Staff endpoints never accept it, and display endpoints never accept a staff session.
/// shortcut: displays are managed per facility by URL (/admin/displays); fold them into device management with MDV-P0-012.
/// </summary>
public static class QueueDisplayAuth
{
    public const string Scheme = "queue-display", Policy = "queue-display", Cookie = "medvya_tv", CookiePath = "/v1/queue";

    /// <summary>The display's id, present only on display principals.</summary>
    public const string DisplayClaim = "display_id";

    /// <summary>Tenant and facility claims, the same names the staff session uses, so tenant isolation applies unchanged.</summary>
    public const string TenantClaim = "tenant_id", FacilityClaim = "facility_id";
}

/// <summary>
/// Live queue TV (MDV-P1-031) at <see cref="Path"/>, for display sessions only. <see cref="Watch"/> joins the display's own
/// facility group, named from the display's claims, never from the client; the TV refetches once it has joined, so nothing
/// changed in between is missed. The message carries nothing at all: the TV refetches its tokens-only queue, so a push can
/// never leak, and a revoked display's refetch is refused. (A revoked TV still connected over WebSockets keeps getting these
/// empty pings until it reconnects, when the display check refuses it; they tell it nothing.)
/// </summary>
public sealed class QueueHub : Hub
{
    public const string Path = "/v1/queue/hub";

    /// <summary>The one message: "something changed, fetch again". No arguments.</summary>
    public const string Changed = "QueueChanged";

    /// <summary>Starts receiving <see cref="Changed"/> for the display's facility. Takes nothing from the client.</summary>
    public async Task Watch()
    {
        var user = Context.User;
        if (!Guid.TryParse(user?.FindFirstValue(QueueDisplayAuth.TenantClaim), out var tenantId)
            || !Guid.TryParse(user?.FindFirstValue(QueueDisplayAuth.FacilityClaim), out var facilityId)
            || user?.FindFirstValue(QueueDisplayAuth.DisplayClaim) is null)
            throw new HubException("Not a display."); // the endpoint policy already requires a display; this never trusts it alone
        await Groups.AddToGroupAsync(Context.ConnectionId, QueueGroups.Of(tenantId, facilityId), Context.ConnectionAborted);
    }
}
