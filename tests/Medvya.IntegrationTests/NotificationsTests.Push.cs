using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Notifications.Application;
using Medvya.Notifications.Domain;
using Medvya.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P5-038 on real PostgreSQL: private push for the notification centre. Synthetic tokens only.</summary>
public sealed partial class NotificationsTests
{
    readonly FakePush pushes = new();

    sealed class FakePush : IPushSender
    {
        readonly List<PushMessage> sent = [];
        public IReadOnlyList<PushMessage> Sent { get { lock (sent) return [.. sent]; } }
        public async Task SendAsync(PushMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Yield(); // lets two runs interleave, as two instances would
            lock (sent) sent.Add(message);
        }
    }

    // Unique per test: the database is shared and a token sits on one session only.
    readonly string AdminPhone = Token("admin"), DeskPhone = Token("desk");
    static string Token(string name) => $"ExponentPushToken[synthetic-{name}-{Guid.NewGuid():N}]";

    /// <summary>A signed-in staff app (native session) of the user, scoped to their membership at the facility, with a push token.</summary>
    async Task<Guid> PhoneAsync(Guid userId, string token, Action<StaffSession>? change = null)
    {
        Guid membership;
        await using (var tenancy = sql.Context(h.Tenant))
            membership = await tenancy.Memberships.Where(m => m.UserId == userId && m.FacilityId == h.Facility).Select(m => m.Id).SingleAsync();
        var now = DateTimeOffset.UtcNow; // sessions run on the real clock
        var session = new StaffSession
        {
            Id = Guid.CreateVersion7(), UserId = userId, Device = "iPhone app", CreatedAt = now, LastSeenAt = now, TenantId = h.Tenant, MembershipId = membership,
            RefreshTokenHash = new byte[32], PushToken = token,
        };
        change?.Invoke(session);
        await using var identity = sql.Identity();
        if (!await identity.Users.AnyAsync(u => u.Id == userId))
            identity.Users.Add(new StaffUser { Id = userId, Issuer = "test-idp", Subject = userId.ToString(), DisplayName = "Staff (synthetic)" });
        identity.Sessions.Add(session);
        await identity.SaveChangesAsync();
        return session.Id;
    }

    /// <summary>An access request item: only practice administration (the admin, not the desk) may see it.</summary>
    async Task<Guid> RaiseAccessRequestAsync()
    {
        await using var db = sql.Notifications(h.Tenant);
        var refId = Guid.NewGuid();
        StaffNotifications.Raise(db, h.Tenant, h.Facility, StaffNotificationKind.AccessRequested, refId, null, h.Clock.GetUtcNow(), $"access-request:{refId:N}");
        await db.SaveChangesAsync();
        return await db.StaffNotifications.Where(n => n.RefId == refId).Select(n => n.Id).SingleAsync();
    }

    Task<int> RunPush() => InScope(sp => sp.GetRequiredService<IStaffPush>().SendDueAsync());

    [Fact]
    public async Task APushCarriesOnlyTheItemIdAndTheFixedTextAndReachesOnlyThoseWhoMaySeeItOnce()
    {
        await PhoneAsync(h.Admin, AdminPhone);
        await PhoneAsync(h.Reception, DeskPhone);
        var item = await RaiseAccessRequestAsync();

        Assert.Equal(1, await RunPush());
        // SAFETY: mutation check: dropping the StaffNotifications.Sees filter in StaffPush sends the desk phone a push too, and this fails.
        var push = Assert.Single(pushes.Sent);
        Assert.Equal(AdminPhone, push.Token);
        Assert.Equal(item, push.Id); // the opaque item id: the app fetches the detail after unlock
        // The payload is the token, the id and the fixed text; there is no field for a kind, a name, a test or a result.
        Assert.Equal(["Token", "Id"], typeof(PushMessage).GetProperties().Select(p => p.Name).ToArray());
        Assert.Equal("You have an update from your clinic", PushMessage.Text);

        Assert.Equal(0, await RunPush()); // pushed once, whatever the number of runs
        Assert.Single(pushes.Sent);
    }

    [Fact]
    public async Task TwoRunsAtOnceStillPushAnItemOnlyOnce()
    {
        await PhoneAsync(h.Admin, AdminPhone);
        for (var i = 0; i < 5; i++) await RaiseAccessRequestAsync();

        // Two instances of the job at the same moment, each in its own scope and connection.
        // SAFETY: mutation check: dropping "x.PushedAt == null" from the claim in StaffPush lets both runs push the same items, and this fails.
        var sent = await Task.WhenAll(RunPush(), RunPush());
        Assert.Equal(5, sent.Sum());
        Assert.Equal(5, pushes.Sent.Select(p => p.Id).Distinct().Count());
        Assert.Equal(5, pushes.Sent.Count);
    }

    [Fact]
    public async Task WithPushesTurnedOffNothingIsSentAndTheListStillShowsIt()
    {
        var session = await PhoneAsync(h.Admin, AdminPhone);
        // Turning pushes off in the app removes its token (DELETE /v1/me/push-token).
        Assert.True(await InScope(sp => sp.GetRequiredService<IStaffSessions>().SetPushTokenAsync(h.Admin, session, null)));
        var item = await RaiseAccessRequestAsync();

        Assert.Equal(0, await RunPush());
        Assert.Empty(pushes.Sent);
        var list = await As(h.Admin, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility));
        Assert.Contains(list.Items, i => i.Id == item && !i.Read);
    }

    [Fact]
    public async Task ARevokedOrEndedPhoneGetsNoPush()
    {
        var revoked = await PhoneAsync(h.Admin, AdminPhone);
        Assert.NotNull(await InScope(sp => sp.GetRequiredService<IStaffSessions>().RevokeAsync(h.Admin, revoked))); // My devices, offboarding, sign-out
        // The query refuses these too, even if a token was left behind.
        // SAFETY: mutation check: removing the IsActive filter or the native check (RefreshTokenHash) from StaffSessions.PushTokensAsync makes this fail.
        await PhoneAsync(h.Admin, Token("revoked"), s => s.RevokedAt = DateTimeOffset.UtcNow);
        await PhoneAsync(h.Admin, Token("idle"), s => s.LastSeenAt = DateTimeOffset.UtcNow - StaffSession.NativeIdleTimeout - TimeSpan.FromMinutes(1));
        await PhoneAsync(h.Admin, Token("browser"), s => s.RefreshTokenHash = null); // a browser session is never pushed to
        await RaiseAccessRequestAsync();

        Assert.Equal(0, await RunPush());
        Assert.Empty(pushes.Sent);
    }

    [Fact]
    public async Task AFailedPushIsNotRetriedAndTheItemStaysInTheList()
    {
        await PhoneAsync(h.Admin, AdminPhone);
        var item = await RaiseAccessRequestAsync();
        Assert.Equal(0, await InScope(sp => ActivatorUtilities.CreateInstance<StaffPush>(sp, new UnconfiguredPushSender()).SendDueAsync()));
        Assert.Equal(0, await RunPush()); // claimed by the failed run: a late push is worse than none
        Assert.Empty(pushes.Sent);
        Assert.Contains((await As(h.Admin, sp => sp.GetRequiredService<IStaffNotifications>().ListAsync(h.Facility))).Items, i => i.Id == item);
    }
}
