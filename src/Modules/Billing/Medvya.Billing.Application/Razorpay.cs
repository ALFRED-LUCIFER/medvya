using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Medvya.Billing.Application;

public enum RazorpayMode { Test, Live }

/// <summary>
/// One clinic's own Razorpay account (ADR-023): pasted by the clinic admin, kept only in the secret store, never in the database or logs.
/// A class, not a record, so no generated ToString ever prints a secret.
/// </summary>
public sealed class RazorpayCredentials(string keyId, string keySecret, string webhookSecret, string webhookKey)
{
    public string KeyId { get; } = keyId;
    public string KeySecret { get; } = keySecret;
    public string WebhookSecret { get; } = webhookSecret;
    /// <summary>Opaque random value in the clinic's webhook URL (/v1/webhooks/razorpay/{key}); picks the tenant, proves nothing.</summary>
    public string WebhookKey { get; } = webhookKey;
    /// <summary>Razorpay key ids say their mode (rzp_test_…, rzp_live_…), so the mode is read from the key, never stored apart from it.</summary>
    public RazorpayMode Mode => KeyId.StartsWith("rzp_live_", StringComparison.Ordinal) ? RazorpayMode.Live : RazorpayMode.Test;

    public override string ToString() => $"Razorpay {Mode} key …{(KeyId.Length > 4 ? KeyId[^4..] : "")}";

    public static string NewWebhookKey() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>64 lowercase hex characters: anything else is refused before it reaches the secret store.</summary>
    public static bool IsWebhookKey(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}

/// <summary>Per-tenant Razorpay credentials: Key Vault in the cloud, configuration (user secrets) in Development.</summary>
public interface IRazorpayCredentialStore
{
    Task<RazorpayCredentials?> GetAsync(Guid tenantId, CancellationToken ct);
    /// <summary>The tenant whose webhook URL carries this key, with its credentials; null for an unknown or malformed key.</summary>
    Task<(Guid TenantId, RazorpayCredentials Credentials)?> FindByWebhookKeyAsync(string webhookKey, CancellationToken ct);
    /// <summary>Saves the clinic's keys. The webhook key is created on the first save and kept on later ones, so the URL in Razorpay stays valid.</summary>
    Task<RazorpayCredentials> SaveAsync(Guid tenantId, string keyId, string keySecret, string webhookSecret, CancellationToken ct);
}

/// <summary>What Settings, Billing shows of the clinic's Razorpay account: never a secret. WebhookPath is the clinic's own webhook path on this host.</summary>
public sealed record RazorpayStatus(bool Connected, RazorpayMode? Mode, string? KeyIdEnding, string? WebhookPath);

/// <summary>The keys the clinic admin pastes. A class, not a record, so no generated ToString ever prints a secret.</summary>
public sealed class ConnectRazorpay
{
    public string? KeyId { get; init; }
    public string? KeySecret { get; init; }
    public string? WebhookSecret { get; init; }
    public override string ToString() => nameof(ConnectRazorpay);
}

/// <summary>
/// The current clinic's Razorpay account (MDV-P4-007 part 2): connecting it (Settings, Billing) and acting on its verified webhooks. Runs in the
/// current tenant; the webhook endpoint enters the tenant its webhook key names before calling here.
/// </summary>
public interface IRazorpayAccount
{
    Task<RazorpayStatus> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Validates and saves the keys (key id rzp_test_… or rzp_live_…, non-empty secrets), audited without their values.</summary>
    Task<RazorpayStatus> ConnectAsync(ConnectRazorpay command, CancellationToken cancellationToken = default);
    /// <summary>
    /// Acts on a webhook whose signature was verified: a replayed event id does nothing; a payment link event re-fetches the link and settles it
    /// (<see cref="IInvoices.ConfirmPaymentLinkAsync"/>). The event is recorded as handled only after that succeeded. False for a replay.
    /// </summary>
    Task<bool> HandleWebhookAsync(RazorpayWebhookEvent webhookEvent, CancellationToken cancellationToken = default);
    /// <summary>Audits a webhook for this clinic's URL whose signature did not match (nothing of the request is kept).</summary>
    Task RecordForgedWebhookAsync(CancellationToken cancellationToken = default);
}

/// <summary>A verified Razorpay webhook: the event id (header x-razorpay-event-id) for de-duplication and the link to fetch again.</summary>
public sealed record RazorpayWebhookEvent(string EventId, string Type, string? PaymentLinkId);

/// <summary>Razorpay webhook checks (ADR-023). Verify the signature over the raw bytes first; parse only a verified body.</summary>
public static class RazorpayWebhook
{
    /// <summary>The clinic's webhook URL path on this host (ADR-023 §6); the clinic pastes origin + this into its Razorpay dashboard.</summary>
    public static string PathFor(string webhookKey) => "/v1/webhooks/razorpay/" + webhookKey;

    /// <summary>X-Razorpay-Signature is the hex HMAC-SHA256 of the raw body under the webhook secret. Constant-time compare.</summary>
    public static bool IsSignatureValid(ReadOnlySpan<byte> rawBody, string? signatureHeader, string webhookSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(webhookSecret)) return false;
        byte[] given;
        try { given = Convert.FromHexString(signatureHeader); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), rawBody);
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>Event type and payment link id from a body whose signature was verified; null when the body or the event id is missing.</summary>
    public static RazorpayWebhookEvent? Parse(ReadOnlySpan<byte> verifiedBody, string? eventIdHeader)
    {
        if (string.IsNullOrEmpty(eventIdHeader)) return null;
        try
        {
            var reader = new Utf8JsonReader(verifiedBody);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var type) || type.ValueKind != JsonValueKind.String) return null;
            string? linkId = null;
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("payment_link", out var link) && link.ValueKind == JsonValueKind.Object
                && link.TryGetProperty("entity", out var entity) && entity.ValueKind == JsonValueKind.Object
                && entity.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                linkId = id.GetString();
            return new(eventIdHeader, type.GetString()!, linkId);
        }
        catch (JsonException) { return null; }
    }
}
