using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Prescribing.Domain;

/// <summary>Who the secure link goes to: the patient (to their registered mobile or email) or a named pharmacy (to the email the doctor enters).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RxShareRecipient>))]
public enum RxShareRecipient { Patient, Pharmacy }

[JsonConverter(typeof(JsonStringEnumConverter<RxShareChannel>))]
public enum RxShareChannel { Sms, Email }

/// <summary>Sent: the link went to the pharmacy. Dispensed: the pharmacy reported it dispensed (entered by the clinic). Cancelled: the clinic withdrew the dispatch.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DispatchState>))]
public enum DispatchState { Sent, Dispensed, Cancelled }

/// <summary>
/// RX SHARE (MDV-P2-020): one secure link to one authorised prescription's stored PDF, sent to the patient or to a named pharmacy. The link token
/// is the tenant id and 32 random bytes (<c>InvitationToken</c>); only its SHA-256 is kept. Opening it needs a one-time code sent to the patient's
/// registered mobile, so a pharmacy opens it only with the patient's say-so. Append-only and hash-chained: a share is never edited.
/// Ids only for the patient: the encounter names them.
/// </summary>
public sealed class RxShare
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid EncounterId { get; init; }
    /// <summary>The authorised version shared; once a later version replaces it, the link opens nothing and says so.</summary>
    [Classification(DataClass.Internal)] public Guid PrescriptionId { get; init; }
    /// <summary>The stored PDF the link hands out, byte for byte.</summary>
    [Classification(DataClass.Internal)] public Guid PdfId { get; init; }
    [Classification(DataClass.Internal)] public RxShareRecipient Recipient { get; init; }
    [Classification(DataClass.Internal)] public RxShareChannel Channel { get; init; }
    [Classification(DataClass.Internal)] public string? PharmacyName { get; init; }
    [Classification(DataClass.Personal)] public string? PharmacyEmail { get; init; }
    [Classification(DataClass.Internal)] public required byte[] LinkHash { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ExpiresAt { get; init; }
    [Classification(DataClass.Internal)] public Guid SharedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SharedAt { get; init; }
}

/// <summary>A one-time code sent by SMS to the patient's registered mobile to open one share. Only a hash is kept; wrong tries are counted.</summary>
public sealed class RxShareCode
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid ShareId { get; init; }
    [Classification(DataClass.Internal)] public required byte[] CodeHash { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset SentAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ExpiresAt { get; init; }
    [Classification(DataClass.Internal)] public int Attempts { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>PHARMACY DISPATCH (MDV-P2-020): one state of a pharmacy share, with who recorded it and when. Append-only: the latest row is the state.</summary>
public sealed class PharmacyDispatchEvent
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid ShareId { get; init; }
    [Classification(DataClass.Internal)] public DispatchState State { get; init; }
    /// <summary>The pharmacy's own reference for the order, as they gave it.</summary>
    [Classification(DataClass.Internal)] public string? PharmacyRef { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? DispensedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid ChangedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; init; }
}

/// <summary>
/// The limits of a share link. shortcut: fixed lifetimes and limits for both countries; make them practice settings if a pilot clinic asks.
/// shortcut: no WhatsApp channel until a WhatsApp Business provider is chosen (with the SMS provider, MDV-P1-045).
/// </summary>
public static class RxShareRules
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(3), CodeLifetime = TimeSpan.FromMinutes(10), CodeInterval = TimeSpan.FromMinutes(1);
    /// <summary>Wrong tries per code, and codes per link: past these the link needs a new share from the clinic.</summary>
    public const int MaxAttempts = 5, MaxCodes = 5, MaxPharmacyName = 100, MaxPharmacyRef = 50;

    public static bool CanMove(DispatchState from, DispatchState to) => from == DispatchState.Sent && to != DispatchState.Sent;

    /// <summary>Six random digits.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>SHA-256 of the code row's id and the code: equal codes of two rows never hash alike.</summary>
    public static byte[] HashCode(Guid codeId, string code) => SHA256.HashData([.. codeId.ToByteArray(), .. Encoding.ASCII.GetBytes(code)]);

    public static bool CodeMatches(RxShareCode row, string? code) =>
        code is { Length: 6 } && code.All(char.IsAsciiDigit) && CryptographicOperations.FixedTimeEquals(row.CodeHash, HashCode(row.Id, code));
}
