using System.Data;
using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Sets SESSION_CONTEXT('tenant_id') read-only on every connection open, so SQL row-level security filters every query.
/// Pooled connections are reset (sp_reset_connection) on reuse, which clears it, so a pooled connection never carries another tenant.
/// Shared by every module DbContext (Tenancy, Audit, …).
/// </summary>
public sealed class TenantSessionInterceptor(ICurrentTenant tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = Command(connection);
        cmd?.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = Command(connection);
        if (cmd is not null) await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    DbCommand? Command(DbConnection connection)
    {
        if (tenant.TenantId is not { } id) return null; // no tenant → RLS returns no tenant rows
        var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sys.sp_set_session_context @key = N'tenant_id', @value = @tenant, @read_only = 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@tenant";
        p.DbType = DbType.Guid;
        p.Value = id;
        cmd.Parameters.Add(p);
        return cmd;
    }
}
