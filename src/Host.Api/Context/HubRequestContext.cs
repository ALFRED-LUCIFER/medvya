using Host.Api.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Host.Api.Context;

/// <summary>
/// Runs around every SignalR hub call and connect. It points the call's <see cref="RequestContext"/> at the connection's own
/// HttpContext, so tenant, facility, user and row-level security come from the principal the connection was opened with, and it
/// checks that the staff session behind it is still live, as every cookie request does: a revoked, idle-expired or switched
/// session ends the connection instead of being served.
/// </summary>
public sealed class HubRequestContext : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);
        await PinAsync(invocationContext.Context, invocationContext.ServiceProvider);
        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        await PinAsync(context.Context, context.ServiceProvider);
        await next(context);
    }

    static async Task PinAsync(HubCallerContext caller, IServiceProvider services)
    {
        var http = caller.GetHttpContext() ?? throw new HubException("Session ended.");
        services.GetRequiredService<RequestContext>().UseConnection(http);
        // Background traffic: a live board must not keep an idle session alive.
        if (!await SessionControl.IsLiveAsync(http.User, services, activity: false, caller.ConnectionAborted))
        {
            caller.Abort();
            throw new HubException("Session ended.");
        }
    }
}
