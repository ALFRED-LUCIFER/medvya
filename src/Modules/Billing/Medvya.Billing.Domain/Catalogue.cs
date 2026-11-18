using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Domain;

public enum ServiceCategory { Consultation, Procedure, Test, Other }

/// <summary>Clinic care is GST-exempt (owner, 16 Nov 2026); a taxable item carries its GST rate. No CGST/SGST split yet.</summary>
public enum TaxTreatment { Exempt, Taxable }

/// <summary>The two consultation services whose doctor prices are the doctors' new-visit and follow-up fees (MDV-P4-002).</summary>
public enum ConsultationKind { New, FollowUp }

/// <summary>How a bill can be paid (design: split payments). Card and UpiDirect are reference only: Medvya never sees a card or UPI PIN.</summary>
public enum PaymentMethod { Cash, Card, UpiDirect, RazorpayLink }

/// <summary>
/// A catalogue item the clinic bills (Services, F-11, MDV-P4-002). Archived, never deleted, so issued invoices keep pointing at it. Its prices are
/// <see cref="PriceVersion"/> rows: the service holds no amount.
/// </summary>
public sealed class Service
{
    public const int MaxName = 200, MaxCode = 20, MaxGstRateBasisPoints = 2800, MaxDurationMinutes = 600;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Public)] public required string Name { get; set; }
    [Classification(DataClass.Internal)] public ServiceCategory Category { get; set; }
    /// <summary>The clinic's own short code, e.g. "ECG".</summary>
    [Classification(DataClass.Internal)] public string? Code { get; set; }
    /// <summary>India's Services Accounting Code, six digits (printed on the bill of supply).</summary>
    [Classification(DataClass.Internal)] public string? Sac { get; set; }
    [Classification(DataClass.Internal)] public TaxTreatment TaxTreatment { get; set; }
    /// <summary>Only when <see cref="TaxTreatment.Taxable"/>: 1800 is 18 %.</summary>
    [Classification(DataClass.Internal)] public int? GstRateBasisPoints { get; set; }
    [Classification(DataClass.Internal)] public int? DurationMinutes { get; set; }
    /// <summary>At most one active service per kind per tenant (unique index).</summary>
    [Classification(DataClass.Internal)] public ConsultationKind? ConsultationKind { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset? ArchivedAt { get; set; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// One price of a service from a moment on, for one doctor (the doctor's membership id in Tenancy, so per doctor per location) or, with no doctor, the clinic
/// default. Immutable: saving a price inserts a version; the database refuses UPDATE and DELETE to the application.
/// </summary>
public sealed class PriceVersion
{
    public const long MaxAmountMinor = 100_000_000;

    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid ServiceId { get; init; }
    [Classification(DataClass.Internal)] public Guid? DoctorId { get; init; }
    [Classification(DataClass.Internal)] public long AmountMinor { get; init; }
    [Classification(DataClass.Internal)] public required string Currency { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset EffectiveFrom { get; init; }
    [Classification(DataClass.Internal)] public Guid? CreatedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The price of a service for <paramref name="doctorId"/> at <paramref name="at"/>: the doctor's latest version already in effect, else the clinic default's,
    /// else none (the line cannot be priced). Of two versions from the same moment the later saved wins.
    /// </summary>
    public static PriceVersion? Resolve(IEnumerable<PriceVersion> versions, Guid? doctorId, DateTimeOffset at)
    {
        var inEffect = versions.Where(v => v.EffectiveFrom <= at).OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.CreatedAt).ToList();
        return (doctorId is null ? null : inEffect.FirstOrDefault(v => v.DoctorId == doctorId)) ?? inEffect.FirstOrDefault(v => v.DoctorId is null);
    }
}

/// <summary>A tenant's billing settings (MDV-P4-002), one row, created with these defaults on first read. Round-off is a country rule, not a setting.</summary>
public sealed class BillingSettings
{
    public const int MaxReasons = 20, MaxReason = 60, MinLinkExpiry = 15, MaxLinkExpiry = 1440;

    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public string InvoicePrefix { get; set; } = "INV";
    [Classification(DataClass.Internal)] public string ReceiptPrefix { get; set; } = "RCT";
    [Classification(DataClass.Internal)] public string CreditNotePrefix { get; set; } = "CN";
    /// <summary>Razorpay links stay off until the clinic connects its own keys (MDV-P4-007).</summary>
    [Classification(DataClass.Internal)] public PaymentMethod[] PaymentMethods { get; set; } = [PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.UpiDirect];
    /// <summary>Preset reasons offered for a discount; free text is still allowed and a reason is always required.</summary>
    [Classification(DataClass.Internal)] public string[] DiscountReasons { get; set; } = [];
    /// <summary>The largest discount staff without practice configuration may give; above it needs the Config capability.</summary>
    [Classification(DataClass.Internal)] public int MaxDiscountPercent { get; set; } = 10;
    [Classification(DataClass.Internal)] public int PaymentLinkExpiryMinutes { get; set; } = 60;
    /// <summary>A refund above this (minor units) is made only by a user with practice configuration (MDV-P4-006); ₹1,000 until the clinic sets its own.</summary>
    [Classification(DataClass.Internal)] public long RefundApprovalAboveMinor { get; set; } = 100_000;
    [Classification(DataClass.Internal)] public Guid? ChangedBy { get; set; }
    [Classification(DataClass.Internal)] public DateTimeOffset ChangedAt { get; set; }
}
