using Medvya.BuildingBlocks.Domain;

namespace Medvya.Billing.Domain;

/// <summary>
/// What a printed billing paper is (P-15): the bill itself (bill of supply or tax invoice), a receipt for what was paid towards it, or a credit note
/// for money given back (MDV-P4-006).
/// </summary>
public enum BillingDocumentKind { Bill, Receipt, CreditNote }

/// <summary>The paper it prints on: A5 for a standard printer, or an 80 mm thermal roll.</summary>
public enum DocumentLayout { A5, Thermal }

/// <summary>
/// An issued billing paper (MDV-P4-005): the PDF written once to Blob with its SHA-256, so a re-print hands out the same bytes. Append-only: the
/// application's role may not change or delete a row (infra/postgres/table-rights.sql). One row per bill, kind, layout and paid total: a receipt for
/// more payments is a new receipt with its own number.
/// </summary>
public sealed class BillingDocument
{
    [Classification(DataClass.Internal)] public Guid Id { get; init; }
    [Classification(DataClass.Internal)] public Guid TenantId { get; init; }
    [Classification(DataClass.Internal)] public Guid FacilityId { get; init; }
    [Classification(DataClass.Internal)] public Guid InvoiceId { get; init; }
    [Classification(DataClass.Internal)] public BillingDocumentKind Kind { get; init; }
    [Classification(DataClass.Internal)] public DocumentLayout Layout { get; init; }
    /// <summary>The bill's number for a bill; the receipt's own number (receipt series) for a receipt, shared by its A5 and thermal copies.</summary>
    [Classification(DataClass.Internal)] public required string Number { get; init; }
    /// <summary>A receipt: the bill's paid total it acknowledges. A bill or credit note: 0 (neither changes once issued).</summary>
    [Classification(DataClass.Internal)] public long PaidMinor { get; init; }
    /// <summary>The credit note printed, for a credit note; null for a bill or receipt.</summary>
    [Classification(DataClass.Internal)] public Guid? CreditNoteId { get; init; }
    /// <summary>Hex SHA-256 of the stored PDF; a re-print checks the blob against it.</summary>
    [Classification(DataClass.Internal)] public required string Sha256 { get; init; }
    /// <summary>Ids only: {tenant}/billing/{id}.</summary>
    [Classification(DataClass.Internal)] public required string BlobKey { get; init; }
    [Classification(DataClass.Internal)] public Guid IssuedBy { get; init; }
    [Classification(DataClass.Internal)] public DateTimeOffset IssuedAt { get; init; }
}
