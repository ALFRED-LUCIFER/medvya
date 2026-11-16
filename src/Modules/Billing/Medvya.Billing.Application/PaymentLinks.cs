using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Application;

public enum PaymentLinkStatus { Created, PartiallyPaid, Paid, Expired, Cancelled }

/// <summary>
/// A hosted payment link to create (ADR-023). The description and notes go to the provider: never clinical data, only the bill number and
/// clinic name ("Bill INV/26-27/00342, Sunrise Clinic") and opaque ids. ReferenceId is unique per attempt (provider limit 40 characters).
/// ExpiresAt must still be at least 15 minutes ahead when the provider receives it.
/// </summary>
public sealed record PaymentLinkRequest(Money Amount, string ReferenceId, string Description, DateTimeOffset ExpiresAt, IReadOnlyDictionary<string, string> Notes);

public sealed record PaymentLink(string Id, string ShortUrl, PaymentLinkStatus Status);

/// <summary>The provider's current view of a link: the source of truth for "paid" (webhooks only say "look again").</summary>
public sealed record PaymentLinkState(string Id, PaymentLinkStatus Status, Money AmountPaid, IReadOnlyList<string> CapturedPaymentIds);

/// <summary>Hosted payment links in the clinic's own provider account (Razorpay in India, MDV-P4-007; the UAE provider reuses it, MDV-P4-008).</summary>
public interface IPaymentLinks
{
    Task<PaymentLink> CreateAsync(Guid tenantId, PaymentLinkRequest request, CancellationToken ct);
    Task<PaymentLinkState> FetchAsync(Guid tenantId, string linkId, CancellationToken ct);
    /// <summary>Cancels a link that is still unpaid and returns its status afterwards: Cancelled, or Paid/Expired when it was too late to cancel.</summary>
    Task<PaymentLinkStatus> CancelAsync(Guid tenantId, string linkId, CancellationToken ct);
}

public enum PaymentProviderError
{
    /// <summary>The clinic has not connected its provider account.</summary>
    NotConnected,
    /// <summary>The provider refused the clinic's keys (wrong or revoked).</summary>
    Unauthorized,
    /// <summary>The provider refused the request (validation, unknown link).</summary>
    Rejected,
    /// <summary>Timeout, rate limit or a provider fault: safe to try again later.</summary>
    Unavailable,
}

/// <summary>A provider call failed. The message carries the error kind and HTTP status only, never the provider's text or our request.</summary>
public sealed class PaymentProviderException(PaymentProviderError error, int? status = null)
    : Exception($"Payment provider call failed: {error}{(status is null ? "" : $" (HTTP {status})")}.")
{
    public PaymentProviderError Error { get; } = error;
    public int? Status { get; } = status;
}
