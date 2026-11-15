using Medvya.Tenancy.Api;
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

    /// <summary>
    /// The Medvya user after acceptance. Null with WrongAccount when the invitation was sent to another address than the signing-in
    /// account's, and null without it when the invitation cannot be used; the sign-in is then refused.
    /// </summary>
    public static async Task<(StaffUser? User, bool WrongAccount)> AcceptAsync(HttpContext http, string token, string issuer, string subject, string? name, string[] emails, StaffUser? existing)
    {
        if (InvitationToken.Read(token) is not { } t) return default;
        var ct = http.RequestAborted;
        await using var scope = http.RequestServices.EnterLinkTenant(t.TenantId);
        var invitations = scope.ServiceProvider.GetRequiredService<IInvitations>();
        // An invitation is for the person it was sent to: the account's email must be the invited address.
        // Checked first so that a dead link, or a link in the wrong hands, never creates an account.
        if (await invitations.IsAddressedToAsync(token, emails, ct) is not { } addressed) return default;
        if (!addressed) return (null, true);
        // shortcut: account creation (identity) and activation (tenancy) are two module transactions; losing a race for the same
        // token leaves an account with no membership, which reaches no clinic data. Share one transaction once an outbox exists.
        // shortcut: the display name comes from the provider's name claim; staff management lets an admin correct it (MDV-P1-008).
        var user = existing ?? await http.RequestServices.GetRequiredService<IStaffUsers>().FindOrCreateAsync(issuer, subject, name ?? "New user", ct);
        return (await invitations.AcceptAsync(token, user.Id, ct) ? user : null, false);
    }
}
