using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Medvya.BuildingBlocks.Domain;

/// <summary>The two hashes a keyed write stores on the record it creates (MDV-P1-047). Neither can be turned back into the key or the request.</summary>
public sealed record IdempotencyHashes(string Key, string Request)
{
    /// <summary>A repeat of the same key is honoured for this long; older keys are cleared when the next keyed write of the tenant runs.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Null when the caller sent no key (the write is not deduplicated). The request hash is salted with the key, a random value the
    /// server never stores, so the request's patient fields cannot be guessed back from the hash.
    /// Throws <see cref="IdempotencyKeyException"/> "invalid" for a key that is not 8 to 64 letters, digits, '-' or '_'.
    /// </summary>
    public static IdempotencyHashes? Of(string? key, object request)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (key.Length is < 8 or > 64 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw new IdempotencyKeyException("invalid");
        return new(Sha256(key), Sha256(key + "\n" + JsonSerializer.Serialize(request)));
    }

    static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>Code "invalid" (malformed key) or "reused" (same key, different request); both are answered with 422.</summary>
public sealed class IdempotencyKeyException(string code) : Exception("Idempotency key refused: " + code)
{
    public string Code { get; } = code;
}
