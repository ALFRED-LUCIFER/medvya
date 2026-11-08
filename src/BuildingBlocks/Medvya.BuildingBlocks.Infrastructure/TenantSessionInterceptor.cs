using System.Data;
using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Sets the session setting medvya.tenant_id on every connection open, so row-level security (MDV-P0-034) filters every query.
/// Npgsql resets a connection (DISCARD ALL) when it returns to the pool, which clears the setting, so a pooled connection never carries another tenant.
/// Without a tenant the settings are cleared explicitly as well (#887), so isolation does not rest on the reset alone; PostgresDataSources
/// refuses a connection string that would skip the reset. Shared by every module DbContext (Tenancy, Audit, …).
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
        var cmd = connection.CreateCommand();
        if (tenant.TenantId is not { } id)
        {
            // No tenant: RLS returns no tenant rows, even on a connection that somehow kept another request's settings.
            cmd.CommandText = "SELECT set_config('medvya.tenant_id', '', false), set_config('medvya.user_id', '', false)";
            return cmd;
        }
        cmd.CommandText = "SELECT set_config('medvya.tenant_id', @tenant, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "@tenant";
        p.DbType = DbType.String; // settings are text; policies cast it back to uuid
        p.Value = id.ToString();
        cmd.Parameters.Add(p);
        return cmd;
    }
}
