using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Medvya.Platform.Application;
using Medvya.Platform.Domain;
using Medvya.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Platform.Infrastructure;

/// <summary>
/// Company sign-on, then a WebAuthn security key (MDV-P5-040). The key must verify its user (PIN or touch with biometrics) and be a
/// device-bound key: a synced passkey (backup eligible) is refused, so the key cannot leave the hardware it was registered on.
/// Each ceremony's options are kept in the session row and consumed by the answer, so a challenge is answered at most once.
/// </summary>
sealed class PlatformSignIn(PlatformDbContext db, IPlatformCaller caller, IFido2 fido2, PlatformOptions options, TimeProvider clock) : IPlatformSignIn
{
    const string Assertion = "a:", Registration = "r:";

    public async Task<ConsoleSession?> SignOnAsync(string issuer, string subject, string? linkToken, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var member = await db.Members.SingleOrDefaultAsync(m => m.Issuer == issuer && m.Subject == subject && m.RemovedAt == null, cancellationToken);
        KeyLink? link = null;
        if (linkToken is not null)
        {
            // A link fits one member: an invitee binds their company account to it; anyone else's account is refused.
            var hash = PlatformAudit.Hash(linkToken);
            link = await db.Links.SingleOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
            if (link?.IsUsable(now) != true) return null;
            var invited = await db.Members.SingleOrDefaultAsync(m => m.Id == link.MemberId && m.RemovedAt == null, cancellationToken);
            if (invited is null || (member is not null && member.Id != invited.Id) || (member is null && invited.Subject is not null)) return null;
            if (member is null)
            {
                invited.Issuer = issuer;
                invited.Subject = subject;
                member = invited;
            }
        }
        else if (member is null && options.BootstrapOwner is { Issuer: { } bi, Subject: { } bs } boot && bi == issuer && bs == subject
                 && !await db.Members.AnyAsync(cancellationToken))
        {
            member = new PlatformMember { DisplayName = boot.DisplayName, Email = boot.Email.ToLowerInvariant(), Role = PlatformRole.Owner, Issuer = issuer, Subject = subject, InvitedAt = now };
            db.Members.Add(member);
            db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "team.bootstrap", "platform_member", member.Id, actor: member.Id));
        }
        if (member is null || !member.HasAccess(now)) return null;

        var session = new PlatformSession { MemberId = member.Id, CreatedAt = now, LastSeenAt = now, KeyLinkId = link?.Id };
        db.Sessions.Add(session);
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "auth.signon", "platform_session", session.Id, actor: member.Id));
        await db.SaveChangesAsync(cancellationToken);
        return new ConsoleSession(member.Id, session.Id);
    }

    public async Task<SessionState?> ValidateAsync(Guid sessionId, Guid memberId, bool activity, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.MemberId == memberId, cancellationToken);
        var member = session is null ? null : await db.Members.SingleAsync(m => m.Id == memberId, cancellationToken);
        if (session is null || member is null || !session.IsActive(now) || !member.HasAccess(now)) return null;
        if (activity)
        {
            session.LastSeenAt = now;
            if (session.KeyVerifiedAt is not null) member.LastActiveAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        var hasKey = await db.Keys.AnyAsync(k => k.MemberId == memberId && k.RevokedAt == null, cancellationToken);
        return new SessionState(member.Id, member.DisplayName, member.Role, session.KeyVerifiedAt is not null, hasKey, await MayRegisterAsync(session, member, cancellationToken));
    }

    public async Task<string?> BeginKeyCheckAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await LiveSessionAsync(sessionId, cancellationToken);
        if (session is null) return null;
        var keys = await db.Keys.AsNoTracking().Where(k => k.MemberId == session.MemberId && k.RevokedAt == null).ToListAsync(cancellationToken);
        if (keys.Count == 0) return null;
        var assertion = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [.. keys.Select(k => new PublicKeyCredentialDescriptor(k.CredentialId))],
            UserVerification = UserVerificationRequirement.Required,
        });
        var json = assertion.ToJson();
        session.PendingCeremony = Assertion + json;
        await db.SaveChangesAsync(cancellationToken);
        return json;
    }

    public async Task<bool> CompleteKeyCheckAsync(Guid sessionId, string responseJson, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var session = await LiveSessionAsync(sessionId, cancellationToken);
        var pending = Take(session, Assertion);
        if (session is null) return false;
        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(responseJson) ?? throw new JsonException();
            var key = pending is null ? null : await db.Keys.SingleOrDefaultAsync(k => k.MemberId == session.MemberId && k.RevokedAt == null && k.CredentialId == response.RawId, cancellationToken);
            if (key is null) return await RefusedAsync(session, now, cancellationToken);
            var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = AssertionOptions.FromJson(pending!),
                StoredPublicKey = key.PublicKey,
                StoredSignatureCounter = (uint)key.SignCount,
                // The key's user handle is the member id it was registered for.
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle is null || new Guid(args.UserHandle) == session.MemberId),
            }, cancellationToken);
            if (result.IsBackedUp) return await RefusedAsync(session, now, cancellationToken);
            key.SignCount = result.SignCount;
            key.LastUsedAt = now;
            session.KeyVerifiedAt = now;
            db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "auth.signin", "platform_session", session.Id, actor: session.MemberId));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception e) when (e is Fido2VerificationException or JsonException or ArgumentException or FormatException)
        {
            return await RefusedAsync(session, now, cancellationToken);
        }
    }

    public async Task<string?> BeginKeyRegistrationAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await LiveSessionAsync(sessionId, cancellationToken);
        var member = session is null ? null : await db.Members.SingleAsync(m => m.Id == session.MemberId, cancellationToken);
        if (session is null || member is null || !await MayRegisterAsync(session, member, cancellationToken)) return null;
        var existing = await db.Keys.AsNoTracking().Where(k => k.MemberId == member.Id && k.RevokedAt == null).Select(k => k.CredentialId).ToListAsync(cancellationToken);
        var creation = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = member.Id.ToByteArray(), Name = member.Email, DisplayName = member.DisplayName },
            ExcludeCredentials = [.. existing.Select(id => new PublicKeyCredentialDescriptor(id))],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                AuthenticatorAttachment = AuthenticatorAttachment.CrossPlatform,
                ResidentKey = ResidentKeyRequirement.Discouraged,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });
        var json = creation.ToJson();
        session.PendingCeremony = Registration + json;
        await db.SaveChangesAsync(cancellationToken);
        return json;
    }

    public async Task<bool> CompleteKeyRegistrationAsync(Guid sessionId, string responseJson, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var session = await LiveSessionAsync(sessionId, cancellationToken);
        var pending = Take(session, Registration);
        var member = session is null ? null : await db.Members.SingleAsync(m => m.Id == session.MemberId, cancellationToken);
        if (session is null || member is null) return false;
        if (pending is null || !await MayRegisterAsync(session, member, cancellationToken)) return await RefusedAsync(session, now, cancellationToken);
        try
        {
            var response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(responseJson) ?? throw new JsonException();
            var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = CredentialCreateOptions.FromJson(pending),
                IsCredentialIdUniqueToUserCallback = async (args, ct) => !await db.Keys.AnyAsync(k => k.CredentialId == args.CredentialId, ct),
            }, cancellationToken);
            if (credential.IsBackupEligible) return await RefusedAsync(session, now, cancellationToken);
            var key = new SecurityKey { MemberId = member.Id, CredentialId = credential.Id, PublicKey = credential.PublicKey, SignCount = credential.SignCount, CreatedAt = now, LastUsedAt = now };
            db.Keys.Add(key);
            if (session.KeyLinkId is { } linkId) (await db.Links.SingleAsync(l => l.Id == linkId, cancellationToken)).UsedAt = now;
            session.KeyVerifiedAt = now; // registering with user verification proves the key is in hand
            db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "key.register", "security_key", key.Id, actor: member.Id));
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception e) when (e is Fido2VerificationException or JsonException or ArgumentException or FormatException)
        {
            return await RefusedAsync(session, now, cancellationToken);
        }
    }

    public async Task SignOutAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.RevokedAt == null, cancellationToken);
        if (session is null) return;
        var now = clock.GetUtcNow();
        session.RevokedAt = now;
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "auth.signout", "platform_session", session.Id, actor: session.MemberId));
        await db.SaveChangesAsync(cancellationToken);
    }

    // A key is registered with the usable link this session signed on with, or, once only, by the configured first owner before any key exists.
    async Task<bool> MayRegisterAsync(PlatformSession session, PlatformMember member, CancellationToken cancellationToken) =>
        session.KeyLinkId is { } linkId
            ? await db.Links.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken) is { } link && link.MemberId == member.Id && link.IsUsable(clock.GetUtcNow())
            : member.InvitedBy is null && member.Role == PlatformRole.Owner && !await db.Keys.AnyAsync(cancellationToken);

    async Task<PlatformSession?> LiveSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        return session is not null && session.IsActive(clock.GetUtcNow()) ? session : null;
    }

    // The pending ceremony of this kind, removed from the session so it cannot be answered twice.
    static string? Take(PlatformSession? session, string kind)
    {
        if (session?.PendingCeremony is not { } p || !p.StartsWith(kind, StringComparison.Ordinal)) return null;
        session.PendingCeremony = null;
        return p[kind.Length..];
    }

    async Task<bool> RefusedAsync(PlatformSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(PlatformAudit.Entry(caller, now, "auth.key.refused", "platform_session", session.Id, actor: session.MemberId));
        await db.SaveChangesAsync(cancellationToken);
        return false;
    }
}
