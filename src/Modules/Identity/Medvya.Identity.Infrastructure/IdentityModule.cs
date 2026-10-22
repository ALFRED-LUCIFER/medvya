using Medvya.Identity.Application;
using Medvya.Identity.Domain;
using Medvya.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Medvya.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString) =>
        services
            .AddDbContext<IdentityDbContext>(o => o.UseSqlServer(connectionString))
            .AddScoped<IStaffUsers, StaffUsers>()
            .AddScoped<IStaffSessions, StaffSessions>();
}

sealed class StaffUsers(IdentityDbContext db) : IStaffUsers
{
    public Task<StaffUser?> FindAsync(string issuer, string subject, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Issuer == issuer && u.Subject == subject, cancellationToken);

    public Task<StaffUser?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    public async Task<StaffUser> FindOrCreateAsync(string issuer, string subject, string displayName, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(issuer, subject, cancellationToken) is { } existing) return existing;
        var user = new StaffUser { Id = Guid.CreateVersion7(), Issuer = issuer, Subject = subject, DisplayName = displayName[..Math.Min(displayName.Length, 200)] };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException)
        {
            // The unique (Issuer, Subject) index refused a concurrent create for the same account: use the one that won.
            db.Entry(user).State = EntityState.Detached;
            return await FindAsync(issuer, subject, cancellationToken) ?? throw new InvalidOperationException("Could not create the user.");
        }
    }
}
