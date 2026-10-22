using Host.Api.Context;
using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;

namespace Host.Api.Auth;

/// <summary>
/// Accepting an invitation during sign-in (MDV-P0-013). "Join" starts /bff/login with the token, which travels in the
/// encrypted OIDC state; after the provider's MFA sign-in this turns the provider account into a Medvya user if new and
/// activates the invited membership, once.
/// </summary>
public static class InvitationSignIn
{
    public const string Item = "medvya.invitation";

    /// <summary>The Medvya user after acceptance, or null when the invitation cannot be used (the sign-in is then refused).</summary>
    public static async Task<StaffUser?> AcceptAsync(HttpContext http, string token, string issuer, string subject, string? name, StaffUser? existing)
    {
        if (InvitationToken.Read(token) is not { } t) return null;
        var ct = http.RequestAborted;
        await using var scope = http.RequestServices.EnterLinkTenant(t.TenantId);
        var invitations = scope.ServiceProvider.GetRequiredService<IInvitations>();
        // shortcut: the link is the bearer credential; the provider's verified email is not matched to the invited address.
        // Add that check with Entra External ID, which emits a verified email claim (MDV-P0-011 Entra spike).
        // Checked first so that a dead link never creates an account.
        if (await invitations.PreviewAsync(token, ct) is null) return null;
        // shortcut: account creation (identity) and activation (tenancy) are two module transactions; losing a race for the same
        // token leaves an account with no membership, which reaches no clinic data. Share one transaction once an outbox exists.
        // shortcut: the display name comes from the provider's name claim; staff management lets an admin correct it (MDV-P1-008).
        var user = existing ?? await http.RequestServices.GetRequiredService<IStaffUsers>().FindOrCreateAsync(issuer, subject, name ?? "New user", ct);
        return await invitations.AcceptAsync(token, user.Id, ct) ? user : null;
    }
}
