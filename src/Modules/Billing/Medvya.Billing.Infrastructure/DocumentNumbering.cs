using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure.Persistence;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// Gap-free numbering: one INSERT ... ON CONFLICT DO UPDATE increments the series row inside the caller's transaction. The row lock serialises
/// concurrent issuers of the same series until the issuing transaction ends; a rollback undoes the increment, so the next issuer
/// gets the same number. A SEQUENCE or IDENTITY would be faster but leaves gaps on rollback, which legal numbering forbids.
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

        // A full series matches no clause: the conflict update is skipped, nothing is returned, so no number is taken and no error dooms the transaction.
        var issued = await db.Database.SqlQuery<long>($"""
            INSERT INTO billing."DocSequences" ("Id", "TenantId", "CountryCode", "DocumentType", "FinancialYear", "Prefix", "NextNumber")
            VALUES ({Guid.CreateVersion7()}, {tenantId}, {rules.CountryCode}, {documentType}, {financialYear}, {prefix}, 2)
            ON CONFLICT ("TenantId", "CountryCode", "DocumentType", "FinancialYear", "Prefix")
            DO UPDATE SET "NextNumber" = billing."DocSequences"."NextNumber" + 1
            WHERE billing."DocSequences"."NextNumber" <= {maxNumber}
            RETURNING ("NextNumber" - 1)::bigint AS "Value"
            """).ToListAsync(cancellationToken);
        if (issued.Count == 0) throw new DocumentNumberingException("series_exhausted");

        var number = issued.Single();
        return new(rules.Format(prefix, financialYear, number), financialYear, number);
    }
}
