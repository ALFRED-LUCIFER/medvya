using System.Buffers.Text;
using System.Security.Cryptography;

namespace Medvya.Identity.Application;

/// <summary>
/// Refresh token = base64url(session id ‖ user id ‖ 32 random bytes). The ids say which session a replayed token belonged to,
/// so it can be ended; they grant nothing alone. A patient session (MDV-P5-033) has no user id and uses Guid.Empty. Only the SHA-256 of the whole token is stored, so it survives restarts and key loss.
/// </summary>
public static class RefreshToken
{
    const int IdBytes = 16, RandomBytes = 32, Length = IdBytes * 2 + RandomBytes;

    public static (string Token, byte[] Hash) New(Guid sessionId, Guid userId)
    {
        var bytes = new byte[Length];
        sessionId.TryWriteBytes(bytes);
        userId.TryWriteBytes(bytes.AsSpan(IdBytes));
        RandomNumberGenerator.Fill(bytes.AsSpan(IdBytes * 2));
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    public static (Guid SessionId, Guid UserId, byte[] Hash)? Read(string? token)
    {
        if (token is null || token.Length > 128 || !Base64Url.IsValid(token, out var length) || length != Length) return null;
        Span<byte> bytes = stackalloc byte[Length];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out var written) || written != Length) return null;
        return (new Guid(bytes[..IdBytes]), new Guid(bytes[IdBytes..(IdBytes * 2)]), SHA256.HashData(bytes));
    }
}
