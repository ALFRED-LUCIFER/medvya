using Medvya.BuildingBlocks.Infrastructure;
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
            .AddDbContext<IdentityDbContext>(o => o.UseNpgsql(PostgresDataSources.For(connectionString)))
            .AddScoped<IStaffUsers, StaffUsers>()
            .AddScoped<IStaffSessions, StaffSessions>()
            .AddScoped<IOnboardingDrafts, OnboardingDrafts>();
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

    public async Task SetDisplayNameAsync(Guid id, string displayName, CancellationToken cancellationToken = default)
    {
        var name = displayName.Trim();
        name = name[..Math.Min(name.Length, 200)];
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, name), cancellationToken);
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

sealed class OnboardingDrafts(IdentityDbContext db) : IOnboardingDrafts
{
    public Task<OnboardingDraft?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.OnboardingDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == userId && d.CompletedAt == null, cancellationToken);

    public async Task<OnboardingDraft> StartAsync(Guid userId, string country, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(userId, cancellationToken) is { } existing) return existing;
        var now = DateTimeOffset.UtcNow;
        // A finished draft's row is reused, so a person who already has a practice can start another.
        var reused = await db.OnboardingDrafts.Where(d => d.UserId == userId).ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Country, country).SetProperty(d => d.Step, 1).SetProperty(d => d.Data, "{}")
            .SetProperty(d => d.UpdatedAt, now).SetProperty(d => d.CompletedAt, (DateTimeOffset?)null), cancellationToken);
        if (reused == 0)
        {
            db.OnboardingDrafts.Add(new OnboardingDraft { UserId = userId, Country = country, Data = "{}", UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
        }
        return await GetAsync(userId, cancellationToken) ?? throw new InvalidOperationException("Could not start the draft.");
    }

    public async Task<bool> SaveAsync(Guid userId, int resumeStep, string data, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.OnboardingDrafts.Where(d => d.UserId == userId && d.CompletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Step, resumeStep).SetProperty(d => d.Data, data).SetProperty(d => d.UpdatedAt, now), cancellationToken) == 1;
    }

    public async Task<bool> CompleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.OnboardingDrafts.Where(d => d.UserId == userId && d.CompletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Data, "{}").SetProperty(d => d.CompletedAt, now).SetProperty(d => d.UpdatedAt, now), cancellationToken) == 1;
    }
}
