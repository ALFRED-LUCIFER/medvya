using System.Text;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Audit.Infrastructure;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-036 against real PostgreSQL: the hash chain, its verifier, the signed daily digest and the alert. Synthetic data only.</summary>
public class LedgerChainTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    internal static readonly byte[] Key = Encoding.UTF8.GetBytes("synthetic-test-digest-key-32bytes");

    internal static async Task<Guid> WriteEvent(PostgresFixture sql, Guid tenant)
    {
        var e = new AuditEvent { Id = Guid.CreateVersion7(), TenantId = tenant, Action = "patient.read", ResourceType = "patient", PatientId = Guid.NewGuid(), Purpose = "treatment", Outcome = AuditOutcome.Allowed, OccurredAt = DateTimeOffset.UtcNow };
        await using var db = sql.Audit(tenant);
        await new AuditLog(db).WriteAsync(e, default);
        return e.Id;
    }

    /// <summary>Runs SQL as the container superuser with the named trigger switched off, the way an attacker with database admin rights would; the trigger is always switched back on.</summary>
    internal static async Task AsSuperuserWithoutTrigger(PostgresFixture sql, string table, string trigger, string statement)
    {
        await using var conn = new NpgsqlConnection(sql.AdminConnectionString);
        await conn.OpenAsync();
        foreach (var command in new[] { $"ALTER TABLE {table} DISABLE TRIGGER \"{trigger}\"", statement, $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER \"{trigger}\"" })
            await new NpgsqlCommand(command, conn).ExecuteNonQueryAsync();
    }

    /// <summary>Verifies as the app, inside the tenant: row-level security is never bypassed to check a chain.</summary>
    internal static async Task<Guid?> Verify(PostgresFixture sql, Guid tenant)
    {
        await using var conn = await sql.OpenAs("app_user", tenant);
        await using var cmd = new NpgsqlCommand("SELECT audit.verify_chain(@t)", conn);
        cmd.Parameters.AddWithValue("t", tenant);
        return await cmd.ExecuteScalarAsync() as Guid?;
    }

    [Fact]
    public async Task EveryRowLinksToThePreviousOneAndAnAlteredRowIsFound()
    {
        var tenant = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await WriteEvent(sql, tenant));
        Assert.Null(await Verify(sql, tenant));

        await using (var conn = new NpgsqlConnection(sql.AdminConnectionString))
        {
            await conn.OpenAsync();
            var rows = new List<(long Seq, string Prev, string Hash)>();
            await using var r = await new NpgsqlCommand($"""SELECT "ChainSeq", encode("PrevHash",'hex'), encode("RowHash",'hex') FROM audit."AuditEvents" WHERE "TenantId" = '{tenant}' ORDER BY 1""", conn).ExecuteReaderAsync();
            while (await r.ReadAsync()) rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
            Assert.Equal([1, 2, 3], rows.Select(x => x.Seq));
            Assert.Equal(new string('0', 64), rows[0].Prev);
            Assert.Equal(rows[0].Hash, rows[1].Prev);
            Assert.Equal(rows[1].Hash, rows[2].Prev);
        }

        // SAFETY: mutation check: changing the hash canonicalisation in ledger.row_hash makes this fail (see PR).
        await AsSuperuserWithoutTrigger(sql, "audit.\"AuditEvents\"", "TR_AuditEvents_AppendOnly", $"""UPDATE audit."AuditEvents" SET "Action" = 'forged' WHERE "Id" = '{ids[1]}'""");
        Assert.Equal(ids[1], await Verify(sql, tenant)); // the first broken row
    }

    [Fact]
    public async Task ARemovedMiddleRowIsFound()
    {
        var tenant = Guid.NewGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await WriteEvent(sql, tenant));
        await AsSuperuserWithoutTrigger(sql, "audit.\"AuditEvents\"", "TR_AuditEvents_AppendOnly", $"""DELETE FROM audit."AuditEvents" WHERE "Id" = '{ids[1]}'""");
        Assert.Equal(ids[2], await Verify(sql, tenant));
    }

    [Fact]
    public async Task ConcurrentInsertsForOneTenantNeverForkTheChain()
    {
        var tenant = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => WriteEvent(sql, tenant)));

        Assert.Null(await Verify(sql, tenant));
        Assert.Equal(Enumerable.Range(1, 24).Select(i => (long)i), (await Seqs(tenant)).Order());
    }

    /// <summary>
    /// MDV-P0-039: the trigger reads the chain head from the (TenantId, ChainSeq) index whatever the planner's costs say. A seq scan plus
    /// sort reads every earlier row per insert, so a bulk insert turns quadratic (the 150,000-row CI seed stalled past 300 s). Index
    /// scans are priced out here to force that plan; the count is exact, so the test does not depend on timing.
    /// </summary>
    [Fact]
    public async Task TheChainHeadIsReadFromTheIndexEvenWhenThePlannerPrefersAScan()
    {
        var tenant = Guid.NewGuid();
        await using var conn = await sql.OpenAs("dbo", tenant);
        await using var tx = await conn.BeginTransactionAsync();
        await new NpgsqlCommand("SET LOCAL random_page_cost = 1000", conn, tx).ExecuteNonQueryAsync();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO audit."AuditEvents" ("Id", "TenantId", "Action", "ResourceType", "Purpose", "Outcome", "OccurredAt")
            SELECT gen_random_uuid(), @t, 'patient.read', 'patient', 'treatment', 'Allowed', now() FROM generate_series(1, 500)
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("t", tenant);
            Assert.Equal(500, await insert.ExecuteNonQueryAsync());
        }
        // SAFETY: mutation check: without "SET enable_sort = off" on ledger.chain() this reads ~125,000 rows (see PR).
        var seqRead = await new NpgsqlCommand("""SELECT seq_tup_read FROM pg_stat_xact_user_tables WHERE schemaname = 'audit' AND relname = 'AuditEvents'""", conn, tx).ExecuteScalarAsync();
        Assert.Equal(0L, seqRead);
    }

    async Task<List<long>> Seqs(Guid tenant)
    {
        await using var conn = new NpgsqlConnection(sql.AdminConnectionString);
        await conn.OpenAsync();
        var seqs = new List<long>();
        await using var r = await new NpgsqlCommand($"""SELECT "ChainSeq" FROM audit."AuditEvents" WHERE "TenantId" = '{tenant}'""", conn).ExecuteReaderAsync();
        while (await r.ReadAsync()) seqs.Add(r.GetInt64(0));
        return seqs;
    }

    [Fact]
    public async Task TheTriggersRefuseTruncateAndSessionReplicationRoleDoesNotSkipThem()
    {
        var tenant = Guid.NewGuid();
        var id = await WriteEvent(sql, tenant);
        // The superuser: it holds DELETE and TRUNCATE rights and may set the replication role, so only the triggers can refuse.
        await using var conn = new NpgsqlConnection(sql.AdminConnectionString);
        await conn.OpenAsync();
        await new NpgsqlCommand("SET session_replication_role = replica", conn).ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand($"""DELETE FROM audit."AuditEvents" WHERE "Id" = '{id}'""", conn).ExecuteNonQueryAsync());
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""TRUNCATE audit."AuditEvents" """, conn).ExecuteNonQueryAsync());
    }
}

public sealed class MemoryDigestStore : IDigestStore
{
    public SortedDictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);
    public Task PutAsync(string name, byte[] content, CancellationToken ct) { Blobs.Add(name, content); return Task.CompletedTask; } // Add throws on a repeat: no overwrite
    public Task<byte[]?> LatestAsync(CancellationToken ct) => Task.FromResult(Blobs.Count == 0 ? null : Blobs.Last().Value);
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>The clean path: a digest is written and a later check passes.</summary>
public class LedgerDigestTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TheDailyDigestIsWrittenSignedAndVerifiesLater()
    {
        var tenant = Guid.NewGuid();
        await LedgerChainTests.WriteEvent(sql, tenant);
        var (store, log) = (new MemoryDigestStore(), new CapturingLogger<LedgerDigests>());
        var digests = new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, store, TimeProvider.System, log);

        await digests.PublishAsync();
        var written = Assert.Single(store.Blobs);
        Assert.Contains(tenant.ToString(), Encoding.UTF8.GetString(written.Value));

        await LedgerChainTests.WriteEvent(sql, tenant); // the chain moves on after the digest
        Assert.Equal(0, await digests.VerifyAsync());
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }
}

/// <summary>A missing digest (MDV-P0-042): a warning in a new environment, an alert once the database records that one was published.</summary>
public class LedgerDigestMissingTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task NoDigestEverIsAWarningButALostOneIsAnAlert()
    {
        await LedgerChainTests.WriteEvent(sql, Guid.NewGuid());
        var log = new CapturingLogger<LedgerDigests>();
        Assert.Equal(0, await new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, new MemoryDigestStore(), TimeProvider.System, log).VerifyAsync());
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Critical);

        var store = new MemoryDigestStore();
        var digests = new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, store, TimeProvider.System, log);
        await digests.PublishAsync();
        var first = store.Blobs.Single();
        await Task.Delay(TimeSpan.FromSeconds(1.1)); // digest names have one-second resolution
        await digests.PublishAsync();

        store.Blobs.Remove(store.Blobs.Keys.Last()); // the newest digest is gone; an older one is still there
        Assert.True(await digests.VerifyAsync() > 0);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("is missing from the digest store"));

        log.Entries.Clear();
        store.Blobs.Remove(first.Key); // every digest is gone
        Assert.True(await digests.VerifyAsync() > 0);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("is missing from the digest store"));
    }
}

/// <summary>Tampering: each case leaves this class's database broken, so assertions look for the specific alert.</summary>
public class LedgerTamperTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TheDigestRecordIsAppendOnly()
    {
        // The superuser holds every right, so only the trigger can refuse.
        await using var conn = new NpgsqlConnection(sql.AdminConnectionString);
        await conn.OpenAsync();
        await new NpgsqlCommand("""INSERT INTO ledger."Digests" VALUES ('test.json', now()) ON CONFLICT DO NOTHING""", conn).ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""DELETE FROM ledger."Digests" """, conn).ExecuteNonQueryAsync());
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""UPDATE ledger."Digests" SET "At" = now() """, conn).ExecuteNonQueryAsync());
        await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand("""TRUNCATE ledger."Digests" """, conn).ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task AnAlteredDigestRaisesTheAlert()
    {
        await LedgerChainTests.WriteEvent(sql, Guid.NewGuid());
        var (store, log) = (new MemoryDigestStore(), new CapturingLogger<LedgerDigests>());
        var digests = new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, store, TimeProvider.System, log);
        await digests.PublishAsync();
        var (name, bytes) = (store.Blobs.Keys.Single(), store.Blobs.Values.Single());
        store.Blobs[name] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\\u0022Seq\\u0022:1", "\\u0022Seq\\u0022:9")); // a head edited, signature kept

        Assert.True(await digests.VerifyAsync() > 0);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("signature does not match"));
    }

    [Fact]
    public async Task ADigestSignedWithAnotherKeyRaisesTheAlert()
    {
        await LedgerChainTests.WriteEvent(sql, Guid.NewGuid());
        var (store, log) = (new MemoryDigestStore(), new CapturingLogger<LedgerDigests>());
        await new LedgerDigests(sql.ConnectionString, Encoding.UTF8.GetBytes("another-key-another-key-another-k"), store, TimeProvider.System, log).PublishAsync();

        Assert.True(await new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, store, TimeProvider.System, log).VerifyAsync() > 0);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains("signature does not match"));
    }

    [Fact]
    public async Task ARemovedLastRowIsCaughtByTheDigestAndRaisesTheAlert()
    {
        var tenant = Guid.NewGuid();
        await LedgerChainTests.WriteEvent(sql, tenant);
        var last = await LedgerChainTests.WriteEvent(sql, tenant);
        var (store, log) = (new MemoryDigestStore(), new CapturingLogger<LedgerDigests>());
        var digests = new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, store, TimeProvider.System, log);
        await digests.PublishAsync();

        // The chain alone cannot see a deleted tail row; the digest can.
        await LedgerChainTests.AsSuperuserWithoutTrigger(sql, "audit.\"AuditEvents\"", "TR_AuditEvents_AppendOnly", $"""DELETE FROM audit."AuditEvents" WHERE "Id" = '{last}'""");
        Assert.Null(await LedgerChainTests.Verify(sql, tenant));

        Assert.True(await digests.VerifyAsync() > 0);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains(tenant.ToString()) && e.Message.Contains("no longer matches"));
    }

    [Fact]
    public async Task AnAlteredRowRaisesTheAlertWithoutAnyPatientData()
    {
        var tenant = Guid.NewGuid();
        var id = await LedgerChainTests.WriteEvent(sql, tenant);
        var log = new CapturingLogger<LedgerDigests>();
        await LedgerChainTests.AsSuperuserWithoutTrigger(sql, "audit.\"AuditEvents\"", "TR_AuditEvents_AppendOnly", $"""UPDATE audit."AuditEvents" SET "Outcome" = 'Denied' WHERE "Id" = '{id}'""");

        Assert.True(await new LedgerDigests(sql.ConnectionString, LedgerChainTests.Key, new MemoryDigestStore(), TimeProvider.System, log).VerifyAsync() > 0);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Critical && e.Message.Contains(id.ToString()) && e.Message.Contains("broken hash chain"));
    }
}
