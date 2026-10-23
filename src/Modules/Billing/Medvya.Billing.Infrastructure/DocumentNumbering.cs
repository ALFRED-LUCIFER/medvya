using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// Gap-free numbering: one MERGE WITH (HOLDLOCK) increments the series row inside the caller's transaction. The lock serialises
/// concurrent issuers of the same series until the issuing transaction ends; a rollback undoes the increment, so the next issuer
/// gets the same number. A SQL SEQUENCE or IDENTITY would be faster but leaves gaps on rollback, which legal numbering forbids.
/// Issuers of one series therefore wait for each other; keep issuing transactions short.
/// </summary>
public sealed class DocumentNumbering(BillingDbContext db, ICurrentTenant tenant) : IDocumentNumbering
{
    public async Task<DocumentNumber> IssueAsync(string countryCode, DocumentType type, string prefix, DateTimeOffset issuedAt, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Document numbers are issued only inside the transaction that stores the document.");
        var rules = BillingCountryRules.For(countryCode);
        rules.CheckPrefix(prefix);
        var financialYear = rules.FinancialYear(issuedAt);
        var maxNumber = rules.MaxNumber(prefix, financialYear);
        var documentType = type.ToString();

        // OUTPUT goes INTO a table variable: SQL Server refuses a bare OUTPUT on a table with a trigger (TR_DocSequences_OnlyMoveForward).
        // A full series matches no clause: no row changes and nothing is output, so no number is taken and no SQL error dooms the transaction.
        var issued = await db.Database.SqlQuery<long>($"""
            DECLARE @issued TABLE (Value bigint NOT NULL);
            MERGE billing.DocSequences WITH (HOLDLOCK) AS s
            USING (SELECT {tenantId} AS TenantId, {rules.CountryCode} AS CountryCode, {documentType} AS DocumentType,
                          {financialYear} AS FinancialYear, {prefix} AS Prefix) AS k
               ON s.TenantId = k.TenantId AND s.CountryCode = k.CountryCode AND s.DocumentType = k.DocumentType
              AND s.FinancialYear = k.FinancialYear AND s.Prefix = k.Prefix
            WHEN MATCHED AND s.NextNumber <= {maxNumber} THEN UPDATE SET NextNumber = s.NextNumber + 1
            WHEN NOT MATCHED THEN INSERT (Id, TenantId, CountryCode, DocumentType, FinancialYear, Prefix, NextNumber)
                 VALUES ({Guid.CreateVersion7()}, k.TenantId, k.CountryCode, k.DocumentType, k.FinancialYear, k.Prefix, 2)
            OUTPUT inserted.NextNumber - 1 INTO @issued;
            SELECT Value FROM @issued;
            """).ToListAsync(cancellationToken);
        if (issued.Count == 0) throw new DocumentNumberingException("series_exhausted");

        var number = issued.Single();
        return new(rules.Format(prefix, financialYear, number), financialYear, number);
    }
}
