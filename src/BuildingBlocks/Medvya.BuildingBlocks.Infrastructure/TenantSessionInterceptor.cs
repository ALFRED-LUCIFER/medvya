using System.Data;
using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Sets the session setting medvya.tenant_id on every connection open, so row-level security (MDV-P0-034) filters every query.
/// Npgsql resets a connection (DISCARD ALL) when it returns to the pool, which clears the setting, so a pooled connection never carries another tenant.
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
        cmd.CommandText = "SELECT set_config('medvya.tenant_id', @tenant, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "@tenant";
        p.DbType = DbType.String; // settings are text; policies cast it back to uuid
        p.Value = id.ToString();
        cmd.Parameters.Add(p);
        return cmd;
    }
}
