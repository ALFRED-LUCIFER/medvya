using System.Security.Claims;

namespace Medvya.BuildingBlocks.Security;

/// <summary>The email addresses an identity provider reports for a signing-in account, used to match an invitation to the person it was sent to.</summary>
public static class ProviderEmails
{
    /// <summary>
    /// Entra sends the sign-in name as preferred_username (always, with the profile scope), and email and upn when the app registration
    /// adds them as optional claims; the development provider sends email. They are trusted because each host's authority is one
    /// directory that Medvya administers (infra/bicep, infra/entra), where only an administrator sets these values. An email the
    /// provider marks as not verified is left out.
    /// shortcut: with a multi-tenant or Entra External ID authority these claims are set by the other directory or by the person; require
    /// the provider's verified-email claim there before such an authority is configured (MDV-P0-011 Entra spike).
    /// </summary>
    public static string[] Of(ClaimsPrincipal principal) =>
    [
        .. principal.FindAll(c => c.Type is "preferred_username" or "upn"
            || (c.Type == "email" && !string.Equals(principal.FindFirst("email_verified")?.Value, "false", StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Value.Trim()).Where(v => v.Contains('@', StringComparison.Ordinal)),
    ];
}
