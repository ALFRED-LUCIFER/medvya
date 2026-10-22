using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Host.Api.Auth;

/// <summary>
/// A minimal OIDC provider for local development and tests only (MDV-P0-005, MDV-P0-011): pick a seeded synthetic user,
/// get an MFA-marked ID token. It lets the real OIDC handler, PKCE and cookie run end to end without Entra.
/// Refuses to exist outside Development; production config points at Entra External ID over https.
/// </summary>
public static class DevIdentityProvider
{
    public const string Issuer = IdentityProviders.Development;
    public const string Path = "/dev-idp";

    static readonly string[] Code = ["code"], Public = ["public"], Rs256 = ["RS256"], S256 = ["S256"], MfaMethods = ["pwd", "mfa"], PasswordOnly = ["pwd"];

    /// <summary>login_hint value that signs in as a brand-new synthetic person, to test accepting an invitation (MDV-P0-013).</summary>
    public const string NewAccount = "new";

    sealed record Grant(string Subject, string? Name, string ClientId, string RedirectUri, string? Nonce, string Challenge, bool Mfa, DateTimeOffset Expires);

    public static WebApplication MapDevIdentityProvider(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("The development identity provider can only run in the Development environment.");

        var key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = Guid.NewGuid().ToString("N") };
        var grants = new ConcurrentDictionary<string, Grant>();
        var dev = app.MapGroup(Path).AllowAnonymous();

        dev.MapGet("/.well-known/openid-configuration", (HttpRequest r) =>
        {
            var self = $"{r.Scheme}://{r.Host}{Path}";
            return Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = Issuer,
                ["authorization_endpoint"] = $"{self}/authorize",
                ["token_endpoint"] = $"{self}/token",
                ["jwks_uri"] = $"{self}/jwks",
                ["response_types_supported"] = Code,
                ["subject_types_supported"] = Public,
                ["id_token_signing_alg_values_supported"] = Rs256,
                ["code_challenge_methods_supported"] = S256,
            });
        });

        dev.MapGet("/jwks", () => Results.Json(new { keys = new[] { JsonWebKeyConverter.ConvertFromRSASecurityKey(key) } }));

        // Without login_hint: a list of seeded users plus "new account". With it: issue a one-time code and return to the app.
        // no_mfa=true signs in without MFA, so tests can prove the app refuses that.
        dev.MapGet("/authorize", async (HttpRequest r, [FromServices] IdentityDbContext db, string client_id, string redirect_uri, string state,
            string code_challenge, string? code_challenge_method, string? nonce, string? login_hint, bool? no_mfa) =>
        {
            if (code_challenge_method != "S256" || !Uri.TryCreate(redirect_uri, UriKind.Absolute, out var back) || !back.IsLoopback)
                return Results.BadRequest("PKCE S256 and a loopback redirect_uri are required.");
            var users = await db.Users.AsNoTracking().Where(u => u.Issuer == Issuer).OrderBy(u => u.DisplayName).ToListAsync();
            if (login_hint is null)
            {
                var html = new StringBuilder("<!doctype html><title>Dev sign-in</title><h1>Development sign-in (synthetic users)</h1><ul>");
                foreach (var u in users)
                    html.Append(CultureInfo.InvariantCulture, $"<li><a href=\"{WebUtility.HtmlEncode($"{r.Path}{r.QueryString}&login_hint={Uri.EscapeDataString(u.Subject)}")}\">{WebUtility.HtmlEncode(u.DisplayName)}</a></li>");
                html.Append(CultureInfo.InvariantCulture, $"<li><a href=\"{WebUtility.HtmlEncode($"{r.Path}{r.QueryString}&login_hint={NewAccount}")}\">New synthetic person (not yet a Medvya user)</a></li>");
                return Results.Content(html.Append("</ul>").ToString(), "text/html");
            }
            // A fresh provider account each time: Medvya refuses it unless the sign-in carries a valid invitation.
            var (subject, name) = login_hint == NewAccount
                ? (Guid.NewGuid().ToString(), (string?)$"New colleague {RandomNumberGenerator.GetHexString(4)} (synthetic)")
                : (login_hint, null);
            if (name is null && users.All(u => u.Subject != subject)) return Results.BadRequest("Unknown user.");
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            grants[code] = new Grant(subject, name, client_id, redirect_uri, nonce, code_challenge, no_mfa != true, DateTimeOffset.UtcNow.AddMinutes(2));
            return Results.Redirect($"{redirect_uri}?code={code}&state={Uri.EscapeDataString(state)}");
        });

        dev.MapPost("/token", async (HttpRequest r) =>
        {
            var f = await r.ReadFormAsync();
            if (f["grant_type"] != "authorization_code" || !grants.TryRemove(f["code"].ToString(), out var g) || g.Expires < DateTimeOffset.UtcNow
                || f["client_id"] != g.ClientId || f["redirect_uri"] != g.RedirectUri
                || Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(f["code_verifier"].ToString()))) != g.Challenge)
                return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
            var now = DateTime.UtcNow;
            var idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = g.ClientId,
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
                Claims = Claims(g),
            });
            return Results.Json(new { id_token = idToken, access_token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), token_type = "Bearer", expires_in = 300 });
        });
        return app;
    }

    static Dictionary<string, object> Claims(Grant g)
    {
        var claims = new Dictionary<string, object> { ["sub"] = g.Subject, ["amr"] = g.Mfa ? MfaMethods : PasswordOnly, ["nonce"] = g.Nonce ?? "" };
        if (g.Name is not null) claims["name"] = g.Name;
        return claims;
    }
}
