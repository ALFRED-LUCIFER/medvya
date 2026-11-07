using System.Buffers.Text;
using System.Security.Cryptography;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Tenancy.Domain;

/// <summary>
/// A single-use link that turns an Invited membership into an Active one for whoever signs in with it (MDV-P0-013).
/// Only the SHA-256 of the token is stored; the token itself exists only in the invite email.
/// </summary>
public sealed class Invitation
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid MembershipId { get; init; }
    [Classification(DataClass.Personal)] public required string Email { get; init; }
    [Classification(DataClass.Internal)] public required byte[] TokenHash { get; init; }
    [Classification(DataClass.Internal)] public Guid InvitedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ExpiresAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? RevokedAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? AcceptedAt { get; set; }
}

public static class InvitationRules
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    /// <summary>Doctors cannot record or sign until their licence is verified (verification ships in MDV-P1-007).</summary>
    public static bool RequiresLicenceVerification(Role role) => role is Role.SoloDoctor or Role.HospitalDoctor;

    /// <summary>Invitations one tenant may send in an hour (new and resent), so the invite email cannot be used to mass-mail strangers.</summary>
    public const int MaxPerHour = 30;

    // Capabilities that do not open patient data in routine work. Break-glass is an audited emergency read, not routine access.
    static readonly Capability[] Administrative = [Capability.DevicePairingFleet, Capability.StaffRolesIntegrations, Capability.AuditLog];

    static bool ReachesPatientData(Func<Capability, Access> access) =>
        Enum.GetValues<Capability>().Except(Administrative).Any(c => access(c) is not (Access.None or Access.BreakGlass));

    /// <summary>
    /// Nobody creates an account that reaches patient data they cannot reach themselves (#875): hospital IT, whose only patient access is
    /// break-glass, cannot invite a receptionist or practice admin and then use that second account to read records.
    /// </summary>
    public static bool CanGrant(Func<Capability, Access> granter, Func<Capability, Access> grantee) =>
        !ReachesPatientData(grantee) || ReachesPatientData(granter);
}

/// <summary>Free-text rules for names that are sent to people outside the practice (invitation emails, slips).</summary>
public static partial class OutboundText
{
    /// <summary>True when the text holds a web address, so a practice or location name cannot carry a phishing link into Medvya's emails (#886).</summary>
    public static bool HasLink(string? text) => text is not null && Link().IsMatch(text);

    [System.Text.RegularExpressions.GeneratedRegex(@"(?i)(https?:|www\.|://|\b[a-z0-9-]+\.(com|net|org|info|io|co|in|ae|xyz|link|ly|me|app|site|online|top|ru|cn)\b)")]
    private static partial System.Text.RegularExpressions.Regex Link();
}

/// <summary>
/// Token = base64url(tenant id (16 bytes) ‖ 32 random bytes): 256 random bits, plus the tenant so the link can be looked up
/// inside that tenant's row-level security. The tenant id alone grants nothing; the stored hash covers all 48 bytes.
/// </summary>
public static class InvitationToken
{
    const int TenantBytes = 16, RandomBytes = 32, Length = TenantBytes + RandomBytes;

    public static (string Token, byte[] Hash) New(Guid tenantId)
    {
        var bytes = new byte[Length];
        tenantId.TryWriteBytes(bytes);
        RandomNumberGenerator.Fill(bytes.AsSpan(TenantBytes));
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>The tenant and stored hash for a token, or null when it is not a well-formed token.</summary>
    public static (Guid TenantId, byte[] Hash)? Read(string? token)
    {
        if (token is null || token.Length > 128 || !Base64Url.IsValid(token, out var length) || length != Length) return null;
        Span<byte> bytes = stackalloc byte[Length];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out var written) || written != Length) return null;
        return (new Guid(bytes[..TenantBytes]), SHA256.HashData(bytes));
    }
}
