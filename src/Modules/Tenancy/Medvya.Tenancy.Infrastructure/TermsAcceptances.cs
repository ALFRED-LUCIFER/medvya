using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Medvya.Tenancy.Infrastructure;

public sealed class TermsAcceptances(TenancyDbContext db, ICurrentTenant tenant, ICurrentUser user, IStaffAccess staff, IAuditLog audit, TimeProvider clock, IConfiguration config)
    : ITermsAcceptance
{
    // Fail closed: only an explicit "false" (Development) switches the requirement off.
    bool Required => !(bool.TryParse(config["Terms:RequireAcceptance"], out var required) && !required);

    async Task<DateTimeOffset?> AcceptedAtAsync(CancellationToken ct) =>
        await db.TermsAcceptances.AsNoTracking().Where(a => a.TermsVersion == TermsPolicy.CurrentVersion)
            .OrderBy(a => a.AcceptedAt).Select(a => (DateTimeOffset?)a.AcceptedAt).FirstOrDefaultAsync(ct);

    public async Task<bool> IsSatisfiedAsync(CancellationToken cancellationToken = default) =>
        tenant.TenantId is not null && (!Required || await AcceptedAtAsync(cancellationToken) is not null);

    public async Task<TermsStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var at = tenant.TenantId is null ? null : await AcceptedAtAsync(cancellationToken);
        return new(TermsPolicy.CurrentVersion, at is not null, at, Required);
    }

    public async Task<bool> AcceptAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        // The owner of a practice: its admin, or the owner-doctor running it. Not hospital IT, not a hospital's clinicians.
        // The DPA binds the account holder as data controller, whatever the state of their licence attestation, and a doctor may book
        // (DoctorLicence); the licence gates recording and signing only.
        if (user.UserId is not { } userId || await staff.CurrentRoleAsync(cancellationToken) is not (Role.PracticeAdmin or Role.SoloDoctor)) return false;
        if (await AcceptedAtAsync(cancellationToken) is not null) return true;

        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.TermsAcceptances.Add(new TermsAcceptance { Id = Guid.CreateVersion7(), TenantId = tenantId, TermsVersion = TermsPolicy.CurrentVersion, AcceptedAt = now, AcceptedBy = userId });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return true; // a second, simultaneous accept: the first one's row and audit event stand
        }
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = userId, Action = "tenant.terms.accept", ResourceType = "tenant",
            ResourceId = tenantId, Purpose = "support", Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}
