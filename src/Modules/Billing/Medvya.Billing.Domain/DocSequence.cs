using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Domain;

/// <summary>Legal document kinds that carry their own number series (design P-15).</summary>
public enum DocumentType { Receipt, Invoice, CreditNote, AdvanceReceipt }

/// <summary>
/// DOC_SEQUENCE: the next legal number of one series, per tenant, country, document type, financial year and configured prefix
/// (MDV-P4-001). Incremented only inside the transaction that issues the document, so a failed issue gives its number back.
/// </summary>
public sealed class DocSequence
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public required string CountryCode { get; init; }
    [Classification(DataClass.Internal)] public DocumentType DocumentType { get; init; }
    /// <summary>Series period from the country rules: "25-26" for India (April–March); empty for a continuous series (UAE).</summary>
    [Classification(DataClass.Internal)] public required string FinancialYear { get; init; }
    [Classification(DataClass.Internal)] public required string Prefix { get; init; }
    /// <summary>The number the next issued document gets; starts at 1.</summary>
    [Classification(DataClass.Internal)] public long NextNumber { get; init; }
}
