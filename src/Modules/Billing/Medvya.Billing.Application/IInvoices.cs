using Medvya.Billing.Domain;

namespace Medvya.Billing.Application;

/// <summary>Who the bill is for, as the desk banner shows it: identity only, never clinical data.</summary>
public sealed record InvoicePatient(Guid Id, string FullName, string Mrn, DateOnly DateOfBirth, string SexAtBirth);

public sealed record InvoiceLineView(
    Guid Id, Guid ServiceId, Guid PriceVersionId, string Description, string? Sac, int Quantity, long UnitMinor, TaxTreatment TaxTreatment, int? GstRateBasisPoints,
    long TaxMinor, long AmountMinor, bool Suggested);

public sealed record PaymentView(
    Guid Id, PaymentMethod Method, long AmountMinor, PaymentState State, string? Reference, string? LinkUrl, DateTimeOffset RecordedAt, DateTimeOffset? ConfirmedAt);

/// <summary>What the desk may do with a bill: the methods the clinic takes, its preset discount reasons and the discount staff may give without practice configuration.</summary>
public sealed record InvoiceOptions(IReadOnlyList<PaymentMethod> PaymentMethods, IReadOnlyList<string> DiscountReasons, int MaxDiscountPercent, bool MayExceedMaxDiscount);

/// <summary>
/// A bill with its lines and payments. <see cref="BalanceMinor"/> is what is still to collect; <see cref="Version"/> guards a draft edit (409 when stale).
/// No clinical notes, diagnoses or visit reasons: billing never carries them (security test).
/// </summary>
public sealed record InvoiceView(
    Guid Id, InvoiceState State, string? Number, DateTimeOffset? IssuedAt, InvoicePatient Patient, Guid? AppointmentId, Guid? DoctorId, string Currency,
    IReadOnlyList<InvoiceLineView> Lines, long SubtotalMinor, long DiscountMinor, string? DiscountReason, long TaxMinor, long RoundOffMinor, long TotalMinor,
    long PaidMinor, long BalanceMinor, IReadOnlyList<PaymentView> Payments, string? CancelReason, DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt,
    InvoiceOptions Options, uint Version);

/// <summary>One row of the bills list.</summary>
public sealed record InvoiceRow(
    Guid Id, InvoiceState State, string? Number, Guid PatientId, string PatientName, string Mrn, string Currency, long TotalMinor, long PaidMinor,
    bool AwaitingProvider, DateTimeOffset CreatedAt, DateTimeOffset? IssuedAt);

/// <summary>The bills list filters. Ids and dates only: a patient is chosen by id, never searched by name in the request (no PHI in URLs or queries).</summary>
public sealed record InvoiceSearch
{
    public InvoiceState? State { get; init; }
    /// <summary>Facility-local days, inclusive, of the bill's creation.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? PatientId { get; init; }
}

public sealed record NewInvoice
{
    public Guid? PatientId { get; init; }
}

public sealed record AddLine
{
    public Guid? ServiceId { get; init; }
    public int? Quantity { get; init; }
    public uint? Version { get; init; }
}

/// <summary>Sets a line's quantity; it also confirms a suggested line.</summary>
public sealed record UpdateLine
{
    public int? Quantity { get; init; }
    public uint? Version { get; init; }
}

public sealed record InvoiceVersion
{
    public uint? Version { get; init; }
}

/// <summary>An amount off the bill with its reason; 0 removes the discount.</summary>
public sealed record SetDiscount
{
    public long? AmountMinor { get; init; }
    public string? Reason { get; init; }
    public uint? Version { get; init; }
}

public sealed record CancelInvoice
{
    public string? Reason { get; init; }
    public uint? Version { get; init; }
}

/// <summary>A payment the desk took itself: cash, card on the terminal (its reference, never the card number) or UPI to the clinic's own QR (the UTR).</summary>
public sealed record RecordPayment
{
    public PaymentMethod? Method { get; init; }
    public long? AmountMinor { get; init; }
    public string? Reference { get; init; }
}

/// <summary>A Razorpay link for the balance: the payment (Pending) and the hosted page's address for the desk to show or copy.</summary>
public sealed record PaymentLinkView(InvoiceView Invoice, Guid PaymentId, string ShortUrl);

/// <summary>
/// Bills and payments (MDV-P4-004, split payments from MDV-P4-009) at the current facility. The endpoints check "Invoices and payments" and the billing
/// flag; tenant filters and row-level security scope every row. Every change and every payment state change is audited in its own transaction, the
/// audit row written last before the commit; no provider call happens inside a transaction.
/// </summary>
/// <remarks>
/// Validation failures throw <see cref="BillingValidationException"/> with field/code pairs: "state:invalid" (the move is not allowed from the bill's state),
/// "amountMinor:over_collected", "method:disabled", "reference:card_number", "discountMinor:over_limit", "lines:unconfirmed" and the like. A stale
/// <c>Version</c> throws EF's DbUpdateConcurrencyException (409). A provider failure throws <see cref="PaymentProviderException"/>.
/// </remarks>
public interface IInvoices
{
    Task<IReadOnlyList<InvoiceRow>> SearchAsync(InvoiceSearch search, CancellationToken cancellationToken = default);

    /// <summary>Null when there is no such bill at this facility.</summary>
    Task<InvoiceView?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>A new draft for a patient; null when the patient is not one of the tenant's.</summary>
    Task<InvoiceView?> CreateAsync(NewInvoice command, CancellationToken cancellationToken = default);

    /// <summary>
    /// The visit's open bill, or a new draft with the suggested consultation line (the visit's doctor, new or follow-up from its visit type) for the desk to
    /// confirm. Null when the visit is not one of this facility's.
    /// </summary>
    Task<InvoiceView?> ForVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    Task<InvoiceView?> AddLineAsync(Guid id, AddLine command, CancellationToken cancellationToken = default);
    Task<InvoiceView?> UpdateLineAsync(Guid id, Guid lineId, UpdateLine command, CancellationToken cancellationToken = default);
    Task<InvoiceView?> RemoveLineAsync(Guid id, Guid lineId, InvoiceVersion command, CancellationToken cancellationToken = default);
    Task<InvoiceView?> SetDiscountAsync(Guid id, SetDiscount command, CancellationToken cancellationToken = default);

    /// <summary>Takes the legal number (DOC_SEQUENCE, the settings' invoice prefix) in the same transaction; the bill is immutable from then on.</summary>
    Task<InvoiceView?> IssueAsync(Guid id, InvoiceVersion command, CancellationToken cancellationToken = default);

    Task<InvoiceView?> CancelAsync(Guid id, CancelInvoice command, CancellationToken cancellationToken = default);

    /// <summary>Records a cash, card or UPI direct payment as Succeeded; a payment beyond the balance is refused.</summary>
    Task<InvoiceView?> RecordPaymentAsync(Guid id, RecordPayment command, CancellationToken cancellationToken = default);

    /// <summary>A Razorpay link for the remaining balance (at most one pending at a time).</summary>
    Task<PaymentLinkView?> CreatePaymentLinkAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>"Check status": asks the provider about a pending link payment (<see cref="ConfirmPaymentLinkAsync"/>).</summary>
    Task<InvoiceView?> CheckPaymentAsync(Guid id, Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a pending link at the provider, then settles the payment from what the provider says (it may have been paid just before).</summary>
    Task<InvoiceView?> CancelPaymentLinkAsync(Guid id, Guid paymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The one place a link payment changes state, for "Check status" and the provider webhook (MDV-P4-007 part 2): fetches the link from the provider
    /// (the source of truth; nothing from a webhook payload is trusted) and moves the pending payment to Succeeded only when the link is paid with exactly the
    /// payment's amount, to Expired when it expired or was cancelled. Audited. Null when no payment of the current tenant has this link.
    /// </summary>
    Task<PaymentState?> ConfirmPaymentLinkAsync(Guid tenantId, string linkId, CancellationToken cancellationToken = default);
}
