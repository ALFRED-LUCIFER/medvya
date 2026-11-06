using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Tenancy.Infrastructure;

public static class TenancyModule
{
    /// <summary>Needs the Audit module (flag changes and invitations are audited) and an IEmailSender (invitations).</summary>
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString, string? blobConnectionString = null)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        // Without a storage connection the logo store refuses every call (fail closed); nothing else needs it.
        services.TryAddSingleton<ILogoStore>(blobConnectionString is null ? new NoLogoStore() : new BlobLogoStore(blobConnectionString));
        return services
            .AddDbContext<TenancyDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IFeatureFlags, FeatureFlags>()
            .AddScoped<ITermsAcceptance, TermsAcceptances>()
            .AddScoped<IFacilityDirectory, FacilityDirectory>()
            .AddScoped<IMembershipDirectory>(sp => new MembershipDirectory(connectionString, sp.GetRequiredService<ICurrentUser>()))
            .AddScoped<IInvitations, Invitations>()
            .AddScoped<IPracticeProvisioning>(sp => ActivatorUtilities.CreateInstance<PracticeProvisioning>(sp, connectionString))
            .AddScoped<IStaffAccess, StaffAccess>()
            .AddScoped<IPracticeSettings, PracticeSettings>()
            .AddScoped<IDoctors, Doctors>()
            .AddScoped<ILicenceReview, LicenceReview>()
            .AddScoped<IStaffRoster, StaffRoster>()
            .AddScoped<IAccessReviews, AccessReviews>()
            .AddScoped<IComplianceFacts, ComplianceFactsReader>();
    }
}
