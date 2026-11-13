using Medvya.BuildingBlocks.Infrastructure;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// Reads across tenants without weakening isolation: its own connection carries the session setting medvya.user_id and <b>no</b>
/// tenant, and the row-level security predicates (migration MembershipDirectory) then show only this user's active
/// memberships and their tenants and facilities. Tenant-scoped connections never set user_id, so they see exactly what they did before.
/// </summary>
public sealed class MembershipDirectory(string connectionString, ICurrentUser user) : IMembershipDirectory
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public async Task<bool> HasEndedMembershipAsync(CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } userId) return false;
        await using var connection = await OpenForUser(connectionString, userId, cancellationToken);
        await using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(connection).Options, new NoTenant());
        // Layer 2 shows this connection only its own membership rows; the filter here repeats it by hand.
        return await db.Memberships.IgnoreQueryFilters().AnyAsync(
            m => m.UserId == userId && (m.Status == MembershipStatus.Suspended || m.Status == MembershipStatus.Offboarded), cancellationToken);
    }

    // A connection that carries the user and no tenant: the only kind that reads across tenants.
    static async Task<NpgsqlConnection> OpenForUser(string connectionString, Guid userId, CancellationToken cancellationToken)
    {
        var connection = await PostgresDataSources.For(connectionString).OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("SELECT set_config('medvya.user_id', @user, false)", connection);
        cmd.Parameters.AddWithValue("user", userId.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    public async Task<IReadOnlyList<MembershipChoice>> ForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } userId) return [];
        await using var connection = await OpenForUser(connectionString, userId, cancellationToken);
        await using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(connection).Options, new NoTenant());
        // The tenant query filters (layer 1) need a current tenant, which this connection deliberately has none of; the same rule
        // is applied here by hand (this user, active only) and enforced by the database (layer 2), as the RLS tests prove.
        return await (
            from m in db.Memberships.IgnoreQueryFilters()
            join f in db.Facilities.IgnoreQueryFilters() on m.FacilityId equals f.Id
            join t in db.Tenants.IgnoreQueryFilters() on m.TenantId equals t.Id
            where m.UserId == userId && m.Status == MembershipStatus.Active
            orderby t.Name, f.Name
            select new MembershipChoice(m.Id, t.Id, t.Name, f.Id, f.Name, m.Role, t.IdleTimeoutMinutes)).ToListAsync(cancellationToken);
    }
}
