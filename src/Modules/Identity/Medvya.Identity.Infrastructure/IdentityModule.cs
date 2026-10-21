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
}
