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
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString, string? blobConnectionString = null, string? blobEndpoint = null) =>
        services.AddOrganisationReview(connectionString, blobConnectionString, blobEndpoint)
            .AddScoped<IFeatureFlags, FeatureFlags>()
            .AddScoped<ITermsAcceptance, TermsAcceptances>()
            .AddScoped<IFacilityDirectory, FacilityDirectory>()
            .AddScoped<IMembershipDirectory>(sp => new MembershipDirectory(connectionString, sp.GetRequiredService<ICurrentUser>()))
            .AddScoped<IInvitations, Invitations>()
            .AddScoped<IPracticeProvisioning, PracticeProvisioning>()
            .AddScoped<IStaffAccess, StaffAccess>()
            .AddScoped<IClinicalAccess, ClinicalAccess>()
            .AddScoped<IAccessRequests, AccessRequests>()
            .AddScoped<IPracticeSettings, PracticeSettings>()
            .AddScoped<IDoctors, Doctors>()
            .AddScoped<IStaffRoster, StaffRoster>()
            .AddScoped<IAccessReviews, AccessReviews>()
            .AddScoped<IComplianceFacts, ComplianceFactsReader>();

    /// <summary>
    /// The organisation request, its state and the review queue (MDV-P1-062, MDV-P1-064), alone: what the platform console registers over its
    /// review login, so no staff, facility or clinical-access service exists there. Needs the Audit module and an IEmailSender.
    /// </summary>
    public static IServiceCollection AddOrganisationReview(this IServiceCollection services, string connectionString, string? blobConnectionString = null, string? blobEndpoint = null)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        // Azure sets the endpoint (managed identity); local runs a connection string. With neither, the logo store refuses every call (fail closed).
        services.TryAddSingleton<ILogoStore>(blobEndpoint is { Length: > 0 } ? new BlobLogoStore(new Uri(blobEndpoint)) : blobConnectionString is null ? new NoLogoStore() : new BlobLogoStore(blobConnectionString));
        return services
            .AddDbContext<TenancyDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IOrganisationState, OrganisationStates>()
            .AddScoped<IOrganisationRequests, OrganisationRequests>()
            .AddScoped<IOrganisationReviews>(sp => new OrganisationReviews(connectionString, sp.GetRequiredService<ICurrentUser>()));
    }
}
