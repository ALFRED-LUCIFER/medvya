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

    /// <summary>Staff roles only: patients join through the portal, never through a staff invitation.</summary>
    public static bool CanInvite(Role role) => role != Role.Patient && Enum.IsDefined(role);

    /// <summary>Doctors cannot record or sign until their licence is verified (verification ships in MDV-P1-007).</summary>
    public static bool RequiresLicenceVerification(Role role) => role is Role.SoloDoctor or Role.HospitalDoctor;
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
