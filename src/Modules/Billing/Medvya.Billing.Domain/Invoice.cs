using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Domain;

/// <summary>Invoice states (docs/03-architecture/state-machines.md "Invoice (P4)"). Pending and Failed belong to a payment, not the invoice.</summary>
public enum InvoiceState { Draft, Issued, PartlyPaid, Paid, Cancelled, Refunded }

/// <summary>A payment's state. Cash, card and UPI direct are recorded Succeeded; a Razorpay link is Pending until the provider says it is paid.</summary>
public enum PaymentState { Pending, Succeeded, Failed, Expired }

/// <summary>
/// A bill (P-06, MDV-P4-004): Draft while the desk edits it, then Issued with its legal number, immutable from then on (corrections are credit
/// notes, MDV-P4-006). Never deleted: a cancelled bill keeps its row and, once issued, its number, so the series has no gaps. Amounts are exact
/// minor units; India adds an invoice-level round-off so the payable total is whole rupees.
/// </summary>
public sealed class Invoice
{
    public const int MaxReason = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    /// <summary>Opaque id of the patient in the Patients module.</summary>
    [Classification(DataClass.Internal)] public Guid PatientId { get; init; }
    /// <summary>The visit billed, when the bill was opened from one (Scheduling); at most one open bill per visit.</summary>
    [Classification(DataClass.Internal)] public Guid? AppointmentId { get; init; }
    /// <summary>The doctor's membership id (Tenancy), whose own prices the lines take.</summary>
    [Classification(DataClass.Internal)] public Guid? DoctorId { get; init; }
    [Classification(DataClass.Internal)] public InvoiceState State { get; set; }
    /// <summary>The legal number (DOC_SEQUENCE), taken when the bill is issued.</summary>
    [Classification(DataClass.Internal)] public string? Number { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? IssuedAt { get; set; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.Internal)] public long SubtotalMinor { get; set; }
    [Classification(DataClass.Internal)] public long DiscountMinor { get; set; }
    /// <summary>Why the discount was given: a preset from the billing settings or the desk's own words. No clinical detail belongs here.</summary>
    [Classification(DataClass.Personal)] public string? DiscountReason { get; set; }
    [Classification(DataClass.Internal)] public long TaxMinor { get; set; }
    /// <summary>The country's invoice-level rounding (India: to the rupee, 50 paise up); 0 where the country does not round.</summary>
    [Classification(DataClass.Internal)] public long RoundOffMinor { get; set; }
    /// <summary>What the patient pays: subtotal − discount + tax + round-off.</summary>
    [Classification(DataClass.Internal)] public long TotalMinor { get; set; }
    /// <summary>The sum of the succeeded payments; never more than the total.</summary>
    [Classification(DataClass.Internal)] public long PaidMinor { get; set; }
    /// <summary>The sum of the bill's credit notes (MDV-P4-006): money given back, never more than was paid. The bill itself stays as issued.</summary>
    [Classification(DataClass.Internal)] public long RefundedMinor { get; set; }
    [Classification(DataClass.Personal)] public string? CancelReason { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? CancelledAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }

    /// <summary>Totals from the lines and the discount. <paramref name="roundOff"/> is the country's rule for the exact payable amount.</summary>
    // The discount is taken off the bill; a bill with a taxable line takes no discount (Invoices.CheckDiscountAsync, until the CA review MDV-P4-031).
    public void Recalculate(IReadOnlyCollection<InvoiceLine> lines, Func<long, long> roundOff)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(roundOff);
        SubtotalMinor = lines.Sum(l => l.AmountMinor);
        TaxMinor = lines.Sum(l => l.TaxMinor);
        var exact = SubtotalMinor - DiscountMinor + TaxMinor;
        RoundOffMinor = roundOff(exact);
        TotalMinor = exact + RoundOffMinor;
    }
}

/// <summary>
/// One line of a bill, priced from the catalogue: the description, SAC, unit price, tax treatment and the price version are copied when the line is
/// added, so a bill keeps what it was priced at when the catalogue changes later (MDV-P4-002).
/// </summary>
public sealed class InvoiceLine
{
    public const int MaxQuantity = 99;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid InvoiceId { get; init; }
    [Classification(DataClass.Internal)] public Guid ServiceId { get; init; }
    [Classification(DataClass.Internal)] public Guid PriceVersionId { get; init; }
    [Classification(DataClass.Public)] public required string Description { get; init; }
    [Classification(DataClass.Internal)] public string? Sac { get; init; }
    [Classification(DataClass.Internal)] public int Quantity { get; set; }
    [Classification(DataClass.Internal)] public long UnitMinor { get; init; }
    [Classification(DataClass.Internal)] public TaxTreatment TaxTreatment { get; init; }
    [Classification(DataClass.Internal)] public int? GstRateBasisPoints { get; init; }
    [Classification(DataClass.Internal)] public long TaxMinor { get; set; }
    /// <summary>Quantity × unit price, before tax.</summary>
    [Classification(DataClass.Internal)] public long AmountMinor { get; set; }
    /// <summary>Proposed from the visit; the desk confirms it before the bill can be issued.</summary>
    [Classification(DataClass.Internal)] public bool Suggested { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Sets the quantity and the amounts that follow from it. Tax per line: amount × rate (basis points) / 10 000, half up.</summary>
    // shortcut: one GST amount per line, no CGST/SGST/IGST split or place of supply; the CA review (MDV-P4-031) settles the printed split.
    public void SetQuantity(int quantity)
    {
        Quantity = quantity;
        AmountMinor = UnitMinor * quantity;
        TaxMinor = TaxTreatment == TaxTreatment.Taxable && GstRateBasisPoints is { } rate ? (AmountMinor * rate + 5_000) / 10_000 : 0;
    }
}

/// <summary>One payment towards a bill; a bill may be paid in parts (cash, card, UPI, a Razorpay link).</summary>
public sealed class Payment
{
    public const int MaxReference = 40;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid InvoiceId { get; init; }
    [Classification(DataClass.Internal)] public PaymentMethod Method { get; init; }
    [Classification(DataClass.Internal)] public long AmountMinor { get; init; }
    [Classification(DataClass.Internal)] public PaymentState State { get; set; }
    /// <summary>The card terminal's reference or the UPI UTR. Never a card number (refused on entry).</summary>
    [Classification(DataClass.Internal)] public string? Reference { get; init; }
    /// <summary>The provider's payment id (pay_…), once the link is paid.</summary>
    [Classification(DataClass.Internal)] public string? ProviderPaymentId { get; set; }
    /// <summary>The provider's link id (plink_…).</summary>
    [Classification(DataClass.Internal)] public string? ProviderLinkId { get; set; }
    /// <summary>The hosted page the patient pays on, for the desk to show or copy.</summary>
    [Classification(DataClass.Internal)] public string? LinkUrl { get; set; }
    [Classification(DataClass.Internal)] public Guid? RecordedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset RecordedAt { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ConfirmedAt { get; set; }
    /// <summary>The location's open cash session when the payment succeeded (MDV-P4-012); null when none was open.</summary>
    [Classification(DataClass.Internal)] public Guid? CashSessionId { get; set; }
}

/// <summary>
/// A refund against a paid bill and the credit note that documents it (MDV-P4-006): its own number from the credit note series, the amount given back,
/// how (cash from the drawer, back to the card on the terminal, or UPI to the patient) and why. Append-only (TR_CreditNotes_AppendOnly): a wrong refund
/// is never edited, and the bill it references never changes.
/// </summary>
public sealed class CreditNote
{
    public const int MaxReason = 200;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid InvoiceId { get; init; }
    /// <summary>The legal number (DOC_SEQUENCE, the settings' credit note prefix).</summary>
    [Classification(DataClass.Internal)] public required string Number { get; init; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.Internal)] public long AmountMinor { get; init; }
    [Classification(DataClass.Internal)] public PaymentMethod Method { get; init; }
    /// <summary>The card terminal's refund reference or the UPI UTR; null for cash. Never a card number (refused on entry).</summary>
    [Classification(DataClass.Internal)] public string? Reference { get; init; }
    /// <summary>Why the money went back. Shown on screen, not printed (it may name the patient); no clinical detail belongs here.</summary>
    [Classification(DataClass.Personal)] public required string Reason { get; init; }
    /// <summary>The location's open cash session when the refund was made (MDV-P4-012); a cash refund comes out of its drawer.</summary>
    [Classification(DataClass.Internal)] public Guid? CashSessionId { get; init; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Every allowed invoice and payment move, in one place; the services ask here and refuse anything else ("state:invalid").</summary>
public static class InvoiceFlow
{
    /// <summary>Lines, quantities and the discount change only on a draft.</summary>
    public static bool CanEdit(InvoiceState state) => state == InvoiceState.Draft;

    public static bool CanIssue(InvoiceState state) => state == InvoiceState.Draft;

    /// <summary>Payments are taken on an issued bill until it is paid, and never after money went back on it (a refund ends collecting).</summary>
    public static bool CanPay(InvoiceState state, long refundedMinor) => state is InvoiceState.Issued or InvoiceState.PartlyPaid && refundedMinor == 0;

    /// <summary>
    /// A draft, or an issued bill that keeps no money: nothing paid (a ₹0 bill is Paid when issued, with nothing paid), or a partly paid bill whose payments
    /// were all given back (a deposit returned when the procedure is called off). It keeps its number.
    /// </summary>
    public static bool CanCancel(InvoiceState state, long paidMinor, long refundedMinor) =>
        state == InvoiceState.Draft || state is InvoiceState.Issued or InvoiceState.Paid or InvoiceState.PartlyPaid && paidMinor == refundedMinor;

    /// <summary>The state an issued bill is in once <paramref name="paidMinor"/> of <paramref name="totalMinor"/> is paid.</summary>
    public static InvoiceState AfterPayment(long totalMinor, long paidMinor) =>
        paidMinor <= 0 && totalMinor > 0 ? InvoiceState.Issued : paidMinor < totalMinor ? InvoiceState.PartlyPaid : InvoiceState.Paid;

    /// <summary>A paid or partly paid bill takes refunds until all it was paid is given back (the amount check is the caller's).</summary>
    public static bool CanRefund(InvoiceState state) => state is InvoiceState.Paid or InvoiceState.PartlyPaid;

    /// <summary>
    /// The state after a refund: a paid bill is Refunded once everything paid is given back, which is final; otherwise the state stays (the credit notes
    /// say how much went back). A partly paid bill stays PartlyPaid: it takes no more payments, and once all is given back it may be cancelled.
    /// </summary>
    public static InvoiceState AfterRefund(InvoiceState state, long paidMinor, long refundedMinor) =>
        state == InvoiceState.Paid && refundedMinor >= paidMinor ? InvoiceState.Refunded : state;

    /// <summary>Only a pending payment moves, and only once: Succeeded, Failed and Expired are final.</summary>
    public static bool CanSettle(PaymentState state) => state == PaymentState.Pending;
}
