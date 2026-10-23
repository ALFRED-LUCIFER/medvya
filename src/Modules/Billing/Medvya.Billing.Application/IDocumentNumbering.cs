using Medvya.Billing.Domain;

namespace Medvya.Billing.Application;

/// <summary>An issued legal number: the printed value plus the series period and position it was taken from.</summary>
public sealed record DocumentNumber(string Value, string FinancialYear, long Sequence);

/// <summary>A legal number was not issued, with a stable code the UI translates ("series_exhausted"); no number was taken.</summary>
public sealed class DocumentNumberingException(string code) : Exception("Document number not issued: " + code)
{
    public string Code { get; } = code;
}

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
    /// <exception cref="DocumentNumberingException">"series_exhausted": the format has no room for the next number; nothing was taken.</exception>
    Task<DocumentNumber> IssueAsync(string countryCode, DocumentType type, string prefix, DateTimeOffset issuedAt, CancellationToken cancellationToken = default);
}
