using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Medvya.Tenancy.Infrastructure;

/// <summary>
/// The review queue's cross-organisation read (MDV-P1-064) without weakening isolation, the way <see cref="MembershipDirectory"/> does it: a connection
/// with medvya.operator and no tenant. The row-level security policies then show the organisation, its request and its documents, nothing else;
/// every other tenant table still sees no row. The endpoint behind it holds the approval capability.
/// </summary>
public sealed class OrganisationReviews(string connectionString, ICurrentUser user) : IOrganisationReviews
{
    sealed class NoTenant : ICurrentTenant { public Guid? TenantId => null; }

    public async Task<IReadOnlyList<QueueItem>> ListAsync(ReviewFilter filter, CancellationToken cancellationToken = default)
    {
        var states = ReviewFilters.States(filter);
        var operatorId = user.UserId ?? throw new InvalidOperationException("No signed-in user.");
        await using var connection = await PostgresDataSources.For(connectionString).OpenConnectionAsync(cancellationToken);
        await using (var cmd = new NpgsqlCommand("SELECT set_config('medvya.operator', @user, false)", connection))
        {
            cmd.Parameters.AddWithValue("user", operatorId.ToString());
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        try
        {
            await using var db = new TenancyDbContext(new DbContextOptionsBuilder<TenancyDbContext>().UseNpgsql(connection).Options, new NoTenant());
            // The query filters need a current tenant, which this connection deliberately has none of; the policies (layer 2) are the guard here.
            var rows = await (
                from t in db.Tenants.IgnoreQueryFilters()
                join r in db.OrganisationRequests.IgnoreQueryFilters() on t.Id equals r.TenantId
                where states.Contains(t.State)
                orderby r.SubmittedAt, t.Name
                select new { t.Id, t.Name, t.CountryCode, r.Type, t.State, r.SubmittedAt, r.LastReason, r.ItContactName, r.ItContactEmail }).ToListAsync(cancellationToken);
            var listed = rows.Select(x => x.Id).ToList();
            var documents = (await db.OrganisationDocuments.IgnoreQueryFilters().Where(d => listed.Contains(d.TenantId)).OrderBy(d => d.UploadedAt).ToListAsync(cancellationToken))
                .ToLookup(d => d.TenantId, d => new OrganisationDocumentView(d.Id, d.Kind, d.ContentType, d.SizeBytes, d.UploadedAt));
            return [.. rows.Select(x => new QueueItem(x.Id, x.Name, x.CountryCode, x.Type, x.State, x.SubmittedAt, x.LastReason, x.ItContactName, x.ItContactEmail, [.. documents[x.Id]]))];
        }
        finally
        {
            // A pooled connection must not carry the setting to the next user of it.
            await using var reset = new NpgsqlCommand("SELECT set_config('medvya.operator', '', false)", connection);
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
