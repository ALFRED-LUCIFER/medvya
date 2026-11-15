using System.Security.Cryptography;
using System.Text;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Identity.Domain;

/// <summary>
/// Patient sign-in by mobile number and SMS code (MDV-P5-033, ADR-011 §2): one row per number that asked for a code or tried one, whether or
/// not anyone with that number is known, so the answers never reveal who has records. It holds the live code's hash, the send count of the
/// current hour and the wrong tries; five wrong codes lock the number. Not row-level secured: it belongs to no clinic, and every query names
/// the number's hash.
/// shortcut: rows are kept after use; purge rows untouched for a day when the data-retention jobs exist.
/// </summary>
public sealed class SmsSignIn
{
    /// <summary>SHA-256 of the E.164 number. Every mobile number can be tried, so this is reversible: personal data, never logged.</summary>
    [Classification(DataClass.Personal)] public required byte[] PhoneHash { get; init; }
    /// <summary>SHA-256 of the phone hash and the live code; null when no code is live.</summary>
    [Classification(DataClass.Internal)] public byte[]? CodeHash { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? CodeExpiresAt { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? LastSentAt { get; set; }
    /// <summary>Codes sent since <see cref="WindowStartedAt"/>; a new window starts an hour later.</summary>
    [Classification(DataClass.Internal)] public int CodesSent { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset WindowStartedAt { get; set; }
    /// <summary>Wrong codes since the last right one or the last lock.</summary>
    [Classification(DataClass.Internal)] public int WrongCodes { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? LockedUntil { get; set; }
}

public static class SmsSignInRules
{
    /// <summary>The SMS text says "10 minutes" (SecureLinkCatalog.SignInCode); change both together.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10), ResendAfter = TimeSpan.FromMinutes(1), Window = TimeSpan.FromHours(1);
    // shortcut: 15 minutes, the same as the staff accounts' Entra smart lockout (MDV-P0-011); no ADR sets the patient period, so it is the owner's call before release.
    public static readonly TimeSpan LockPeriod = TimeSpan.FromMinutes(15);
    public const int MaxWrongCodes = 5, MaxCodesPerWindow = 5;

    public static byte[] HashPhone(string e164) => SHA256.HashData(Encoding.ASCII.GetBytes("medvya.sms-sign-in:" + e164));

    /// <summary>Six random digits.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static byte[] HashCode(byte[] phoneHash, string code) => SHA256.HashData([.. phoneHash, .. Encoding.ASCII.GetBytes(code)]);

    public static bool CodeMatches(SmsSignIn row, string? code, DateTimeOffset now) =>
        row.CodeHash is not null && row.CodeExpiresAt > now && code is { Length: 6 } && code.All(char.IsAsciiDigit)
        && CryptographicOperations.FixedTimeEquals(row.CodeHash, HashCode(row.PhoneHash, code));

    /// <summary>Whole minutes until <paramref name="until"/>, at least 1, for "Try again in N minutes".</summary>
    public static int MinutesLeft(DateTimeOffset until, DateTimeOffset now) => Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
}
