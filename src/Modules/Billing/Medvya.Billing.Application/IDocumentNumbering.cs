using Medvya.Billing.Domain;

namespace Medvya.Billing.Application;

/// <summary>An issued legal number: the printed value plus the series period and position it was taken from.</summary>
public sealed record DocumentNumber(string Value, string FinancialYear, long Sequence);

/// <summary>
/// Gap-free legal document numbers (DOC_SEQUENCE, MDV-P4-001) for the current tenant.
/// shortcut: the country code and prefix are trusted from the caller; the issuing service (MDV-P4-004) takes them from the
/// tenant and the facility's configured series once practice settings exist.
/// </summary>
public interface IDocumentNumbering
{
    /// <summary>
    /// Takes the next number of the series. Must run inside the transaction that stores the issued document: the series row
    /// stays locked until that transaction ends, and a rollback gives the number back, so committed numbers have no gaps.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is open, or there is no tenant.</exception>
    Task<DocumentNumber> IssueAsync(string countryCode, DocumentType type, string prefix, DateTimeOffset issuedAt, CancellationToken cancellationToken = default);
}
