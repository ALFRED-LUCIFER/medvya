using Medvya.Billing.Domain;

namespace Medvya.Billing.Application;

/// <summary>A printed billing paper: the PDF bytes and their hex SHA-256.</summary>
public sealed record BillingPdf(byte[] Content, string Sha256);

/// <summary>
/// The bill and its receipt as PDFs (MDV-P4-005, P-15), India: A5 or 80 mm thermal. The first print issues the paper (a receipt takes its number from the
/// receipt series), stores it in Blob with its hash and audits it; every later print of the same paper returns the stored bytes, audited as a re-print.
/// </summary>
/// <remarks>"state:invalid": a draft or cancelled bill prints nothing. "payment:none": a receipt needs a payment.</remarks>
public interface IBillingDocuments
{
    /// <summary>Null when there is no such bill at this facility.</summary>
    Task<BillingPdf?> PdfAsync(Guid invoiceId, BillingDocumentKind kind, DocumentLayout layout, CancellationToken cancellationToken = default);
}
