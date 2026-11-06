using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Medvya.Audit.Infrastructure;

/// <summary>Where signed digests are kept. Blob in production: a container with a time-based retention policy (Bicep, MDV-P0-038), so a written digest cannot be changed or deleted.</summary>
public interface IDigestStore
{
    /// <summary>Writes a new digest; never overwrites.</summary>
    Task PutAsync(string name, byte[] content, CancellationToken ct);
    /// <summary>The newest digest, or null when none was written yet.</summary>
    Task<byte[]?> LatestAsync(CancellationToken ct);
}

public sealed class BlobDigestStore(string connectionString, string container = "ledger-digests") : IDigestStore
{
    readonly BlobContainerClient client = new(connectionString, container);

    public async Task PutAsync(string name, byte[] content, CancellationToken ct)
    {
        await client.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await client.GetBlobClient(name).UploadAsync(new BinaryData(content), new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, ct);
    }

    public async Task<byte[]?> LatestAsync(CancellationToken ct)
    {
        string? newest = null;
        await foreach (var b in client.GetBlobsAsync(cancellationToken: ct)) if (string.CompareOrdinal(b.Name, newest) > 0) newest = b.Name; // names sort by time
        return newest is null ? null : (await client.GetBlobClient(newest).DownloadContentAsync(ct)).Value.Content.ToArray();
    }
}

/// <summary>
/// Daily signed digest of every ledger chain head (MDV-P0-036) and the check against it. The digest is JSON {payload, hmac}: payload lists
/// (table, tenant, last sequence, last hash) and hmac is HMAC-SHA256 of the payload text with the digest key (Key Vault in the cloud, a local key
/// in Development). Hashes are integrity evidence, not a legally qualified signature. Ids and hashes only, no PHI.
/// Row-level security is never bypassed: the tenants come from the ids-only ledger."Chains" list, and each tenant's chains are read with
/// medvya.tenant_id set to that tenant. shortcut: heads are a group-by per table and a round trip per tenant; keep a head row per chain
/// when tables or tenant counts are large.
/// </summary>
public sealed partial class LedgerDigests(string connectionString, byte[] key, IDigestStore store, TimeProvider clock, ILogger<LedgerDigests> log)
{
    sealed record Head(string Table, Guid Tenant, long Seq, string Hash);
    sealed record Payload(DateTimeOffset At, List<Head> Heads);
    sealed record Signed(string Payload, string Hmac);

    string Sign(string payload) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));

    async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    static async Task InTenantAsync(NpgsqlConnection conn, Guid tenant, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('medvya.tenant_id', @t, false)", conn);
        cmd.Parameters.AddWithValue("t", tenant.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task<List<Head>> HeadsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var tenants = new List<Guid>();
        await using (var list = new NpgsqlCommand("""SELECT DISTINCT "TenantId" FROM ledger."Chains" ORDER BY 1""", conn))
        await using (var r = await list.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) tenants.Add(r.GetGuid(0));

        var heads = new List<Head>();
        foreach (var tenant in tenants)
        {
            await InTenantAsync(conn, tenant, ct);
            await using var cmd = new NpgsqlCommand("SELECT tbl, tenant, seq, hash FROM ledger.heads() ORDER BY 1, 2", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) heads.Add(new Head(r.GetString(0), r.GetGuid(1), r.GetInt64(2), Convert.ToHexStringLower((byte[])r[3])));
        }
        return heads;
    }

    /// <summary>Writes the signed digest of the chain heads as of now.</summary>
    public async Task PublishAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var now = clock.GetUtcNow();
        var payload = JsonSerializer.Serialize(new Payload(now, await HeadsAsync(conn, ct)));
        await store.PutAsync($"{now:yyyy/MM/dd/HHmmss}.json", JsonSerializer.SerializeToUtf8Bytes(new Signed(payload, Sign(payload))), ct);
    }

    /// <summary>
    /// Re-reads every chain and compares with the latest digest. Returns the number of problems found; each one is logged at Critical
    /// (the alert) with table and tenant ids only. A digest that is unreadable or altered is a problem; no digest at all yet is only a
    /// warning (a new environment).
    /// </summary>
    public async Task<int> VerifyAsync(CancellationToken ct = default)
    {
        var problems = 0;
        await using var conn = await OpenAsync(ct);
        var chains = (await HeadsAsync(conn, ct)).Select(h => (h.Table, h.Tenant)).ToHashSet();

        var raw = await store.LatestAsync(ct);
        if (raw is null) NoDigest(log);
        else
        {
            Signed? signed = null;
            Payload? digest = null;
            try
            {
                signed = JsonSerializer.Deserialize<Signed>(raw);
                digest = signed is null ? null : JsonSerializer.Deserialize<Payload>(signed.Payload);
            }
            catch (JsonException) { }
            if (signed is null || digest is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(signed.Payload)), Encoding.ASCII.GetBytes(signed.Hmac)))
            {
                problems++;
                DigestInvalid(log);
            }
            else
                foreach (var h in digest.Heads)
                {
                    chains.Add((h.Table, h.Tenant));
                    await InTenantAsync(conn, h.Tenant, ct);
                    await using var cmd = new NpgsqlCommand("SELECT ledger.hash_at(@t, @tenant, @seq)", conn);
                    cmd.Parameters.AddWithValue("t", h.Table);
                    cmd.Parameters.AddWithValue("tenant", h.Tenant);
                    cmd.Parameters.AddWithValue("seq", h.Seq);
                    if (await cmd.ExecuteScalarAsync(ct) is not byte[] at || Convert.ToHexStringLower(at) != h.Hash)
                    {
                        problems++;
                        HeadChanged(log, h.Table, h.Tenant, h.Seq);
                    }
                }
        }

        foreach (var (table, tenant) in chains)
        {
            await InTenantAsync(conn, tenant, ct);
            await using var cmd = new NpgsqlCommand("SELECT ledger.verify_chain(@t, @tenant)", conn);
            cmd.Parameters.AddWithValue("t", table);
            cmd.Parameters.AddWithValue("tenant", tenant);
            if (await cmd.ExecuteScalarAsync(ct) is Guid broken)
            {
                problems++;
                ChainBroken(log, table, tenant, broken);
            }
        }
        return problems;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No ledger digest has been written yet; chains were verified without one.")]
    static partial void NoDigest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "LEDGER ALERT: the latest digest is unreadable or its signature does not match.")]
    static partial void DigestInvalid(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "LEDGER ALERT: {Table} for tenant {TenantId} no longer matches the signed digest at sequence {Seq} (row removed or changed).")]
    static partial void HeadChanged(ILogger logger, string table, Guid tenantId, long seq);

    [LoggerMessage(Level = LogLevel.Critical, Message = "LEDGER ALERT: {Table} for tenant {TenantId} has a broken hash chain at row {RowId}.")]
    static partial void ChainBroken(ILogger logger, string table, Guid tenantId, Guid rowId);
}

/// <summary>The two recurring jobs (registered in Program.cs next to the notification job). A failed verification fails the job so Hangfire and its alerts show it.</summary>
public sealed class LedgerJobs(LedgerDigests digests)
{
    public const string PublishJobId = "ledger-digest";
    public const string VerifyJobId = "ledger-verify";

    public Task PublishAsync(CancellationToken ct) => digests.PublishAsync(ct);

    public async Task VerifyAsync(CancellationToken ct)
    {
        if (await digests.VerifyAsync(ct) is var problems and > 0) throw new InvalidOperationException($"Ledger verification found {problems} problem(s).");
    }
}
