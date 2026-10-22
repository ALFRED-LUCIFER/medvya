using System.Collections.Concurrent;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.Billing.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P4-001 on SQL Server 2022: gap-free legal numbers under concurrency, rollover, tenant isolation, no floating-point columns.</summary>
public class BillingNumberingTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    static readonly DateTimeOffset October = new(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);

    /// <summary>One issuing transaction: takes a number, then commits or (simulating a failed issue) ends without committing.</summary>
    async Task<DocumentNumber> Issue(Guid tenant, string country, DocumentType type, string prefix, DateTimeOffset at, bool commit = true)
    {
        await using var db = sql.Billing(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();
        var number = await new DocumentNumbering(db, new SqlServerFixture.FixedTenant(tenant)).IssueAsync(country, type, prefix, at);
        await Task.Delay(2); // the rest of the issuing work, while the series is held
        if (commit) await tx.CommitAsync();
        return number;
    }

    [Fact]
    public async Task ConcurrentIssuingGivesConsecutiveNumbersWithoutGapsOrDuplicates()
    {
        var tenant = Guid.NewGuid();
        var committed = new ConcurrentBag<DocumentNumber>();
        // 100 invoices issued at once from a series that does not exist yet, plus 10 issues that fail after taking a number.
        await Task.WhenAll(Enumerable.Range(0, 110).Select(i => Task.Run(async () =>
        {
            var fails = i % 11 == 5;
            var number = await Issue(tenant, "IN", DocumentType.Invoice, "INV", October, commit: !fails);
            if (!fails) committed.Add(number);
        })));

        Assert.Equal(Enumerable.Range(1, 100).Select(n => (long)n), committed.Select(n => n.Sequence).Order());
        Assert.Equal(100, committed.Select(n => n.Value).Distinct().Count());
        Assert.Contains(committed, n => n.Value == "INV/26-27/00100");
        await using var db = sql.Billing(tenant);
        Assert.Equal(101, (await db.DocSequences.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task AFailedIssueGivesItsNumberBack()
    {
        var tenant = Guid.NewGuid();
        Assert.Equal("RCT/26-27/00001", (await Issue(tenant, "IN", DocumentType.Receipt, "RCT", October)).Value);
        Assert.Equal(2, (await Issue(tenant, "IN", DocumentType.Receipt, "RCT", October, commit: false)).Sequence);
        Assert.Equal("RCT/26-27/00002", (await Issue(tenant, "IN", DocumentType.Receipt, "RCT", October)).Value);
    }

    [Fact]
    public async Task IssuingOutsideATransactionIsRefused()
    {
        var tenant = Guid.NewGuid();
        await using var db = sql.Billing(tenant);
        var numbering = new DocumentNumbering(db, new SqlServerFixture.FixedTenant(tenant));
        await Assert.ThrowsAsync<InvalidOperationException>(() => numbering.IssueAsync("IN", DocumentType.Invoice, "INV", October));
        Assert.Empty(await db.DocSequences.ToListAsync());
    }

    [Fact]
    public async Task FinancialYearRolloverStartsANewSeriesPerPrefix()
    {
        var tenant = Guid.NewGuid();
        var lastMinute = new DateTimeOffset(2027, 3, 31, 23, 59, 0, TimeSpan.FromHours(5.5));
        var firstMinute = new DateTimeOffset(2027, 4, 1, 0, 0, 0, TimeSpan.FromHours(5.5));

        Assert.Equal("INV/26-27/00001", (await Issue(tenant, "IN", DocumentType.Invoice, "INV", lastMinute)).Value);
        Assert.Equal("INV/26-27/00002", (await Issue(tenant, "IN", DocumentType.Invoice, "INV", lastMinute)).Value);
        Assert.Equal("INV/27-28/00001", (await Issue(tenant, "IN", DocumentType.Invoice, "INV", firstMinute)).Value);
        Assert.Equal("INVB/27-28/00001", (await Issue(tenant, "IN", DocumentType.Invoice, "INVB", firstMinute)).Value); // own series per prefix
        Assert.Equal("INV/27-28/00001", (await Issue(tenant, "IN", DocumentType.CreditNote, "INV", firstMinute)).Value); // and per document type
        Assert.Equal("INV/27-28/00002", (await Issue(tenant, "IN", DocumentType.Invoice, "INV", firstMinute)).Value);

        // UAE: one continuous series (the design's numbers carry no year).
        Assert.Equal("TI-AD-00001", (await Issue(tenant, "AE", DocumentType.Invoice, "TI-AD", new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.FromHours(4)))).Value);
        Assert.Equal("TI-AD-00002", (await Issue(tenant, "AE", DocumentType.Invoice, "TI-AD", new DateTimeOffset(2027, 1, 1, 0, 30, 0, TimeSpan.FromHours(4)))).Value);
    }

    [Fact]
    public async Task EachTenantHasItsOwnSeriesAndCannotSeeOrWriteAnothers()
    {
        Guid tenantA = Guid.NewGuid(), tenantB = Guid.NewGuid();
        Assert.Equal(1, (await Issue(tenantA, "IN", DocumentType.Invoice, "INV", October)).Sequence);
        Assert.Equal(1, (await Issue(tenantB, "IN", DocumentType.Invoice, "INV", October)).Sequence);
        Assert.Equal(2, (await Issue(tenantA, "IN", DocumentType.Invoice, "INV", October)).Sequence);

        // Row-level security holds with the EF query filter off and for hand-written SQL.
        await using var inA = sql.Billing(tenantA);
        Assert.All(await inA.DocSequences.IgnoreQueryFilters().ToListAsync(), s => Assert.Equal(tenantA, s.TenantId));
        Assert.All(await inA.Database.SqlQuery<Guid>($"SELECT TenantId AS Value FROM billing.DocSequences").ToListAsync(), id => Assert.Equal(tenantA, id));

        // Tenant A can neither plant a row in B's name nor move its own series to B.
        inA.DocSequences.Add(new DocSequence { Id = Guid.NewGuid(), TenantId = tenantB, CountryCode = "IN", DocumentType = DocumentType.Receipt, FinancialYear = "26-27", Prefix = "RCT", NextNumber = 1 });
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => inA.SaveChangesAsync());
        Assert.Contains("block predicate", Assert.IsType<SqlException>(insert.InnerException).Message, StringComparison.Ordinal);
        await using var inA2 = sql.Billing(tenantA);
        var move = await Assert.ThrowsAsync<SqlException>(() => inA2.Database.ExecuteSqlAsync($"UPDATE billing.DocSequences SET TenantId = {tenantB}, Prefix = N'MOVE'"));
        Assert.Contains("block predicate", move.Message, StringComparison.Ordinal);

        // Without a tenant there are no rows and no numbers.
        await using var none = sql.Billing(null);
        Assert.Empty(await none.DocSequences.IgnoreQueryFilters().ToListAsync());
        await using var tx = await none.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DocumentNumbering(none, new SqlServerFixture.FixedTenant(null)).IssueAsync("IN", DocumentType.Invoice, "INV", October));
    }

    [Fact]
    public async Task TheAppRoleCannotRewindSkipOrMoveASeries()
    {
        var tenant = Guid.NewGuid();
        await Issue(tenant, "IN", DocumentType.Invoice, "INV", October);
        await Issue(tenant, "IN", DocumentType.Invoice, "INV", October);

        // The billing role has UPDATE (the MERGE needs it), so the trigger is what keeps the series legal.
        foreach (var statement in new[]
        {
            "UPDATE billing.DocSequences SET NextNumber = 1",                // rewind: numbers 1 and 2 would be issued again
            "UPDATE billing.DocSequences SET NextNumber = NextNumber + 5",   // skip: a gap
            "UPDATE billing.DocSequences SET FinancialYear = N'27-28'",      // move the series to another year
            "UPDATE billing.DocSequences SET Prefix = N'INVB', NextNumber = NextNumber + 1",
            "INSERT INTO billing.DocSequences (Id, TenantId, CountryCode, DocumentType, FinancialYear, Prefix, NextNumber) " +
                "VALUES (NEWID(), CAST(SESSION_CONTEXT(N'tenant_id') AS uniqueidentifier), N'IN', N'Receipt', N'26-27', N'RCT', 50)", // starts with a gap
        })
        {
            await using var conn = await sql.OpenAs("billing_user", tenant);
            await using var cmd = new SqlCommand(statement, conn);
            var refused = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
            Assert.True(refused.Number == 51000, $"{statement}: {refused.Number} {refused.Message}");
        }

        Assert.Equal("INV/26-27/00003", (await Issue(tenant, "IN", DocumentType.Invoice, "INV", October)).Value);
    }

    [Fact]
    public async Task NoColumnIsFloatingPointOrDecimal()
    {
        // AGENTS.md 3.4, database half of FloatingPointMoneyTests: every module's migrations have run in the fixture.
        await using var db = sql.Billing(null);
        var columns = await db.Database.SqlQuery<string>($"""
            SELECT SCHEMA_NAME(t.schema_id) + '.' + t.name + '.' + c.name AS Value
            FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0 AND ty.name IN ('float', 'real', 'decimal', 'numeric', 'money', 'smallmoney')
            """).ToListAsync();
        Assert.Empty(columns);
        Assert.Contains("NextNumber", await db.Database.SqlQuery<string>($"SELECT name AS Value FROM sys.columns WHERE object_id = OBJECT_ID(N'billing.DocSequences')").ToListAsync());
    }
}
