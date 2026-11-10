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
    /// <summary>
    /// The organisation review path for the platform console (ADR-014 note, MDV-P1-064), and nothing else of this module: the request, its documents
    /// and the one place the state changes. Runs as the narrow login medvya_review (roles.sql), not the clinic application's. Needs the Audit module
    /// on the same login and an IEmailSender (the decision email).
    /// </summary>
    public static IServiceCollection AddOrganisationReviewPath(this IServiceCollection services, string connectionString, string? blobConnectionString = null, string? blobEndpoint = null)
    {
        AddShared(services, connectionString, blobConnectionString, blobEndpoint);
        return services
            .AddScoped<ReviewContext>()
            .AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<ReviewContext>())
            .AddScoped<ICurrentUser>(sp => sp.GetRequiredService<ReviewContext>())
            .AddScoped<IOrganisationState, OrganisationStates>()
            .AddScoped<IOrganisationRequests, OrganisationRequests>()
            .AddScoped<IOrganisationReviews>(sp => new OrganisationReviews(connectionString, sp.GetRequiredService<ICurrentUser>()))
            .AddSingleton<IOrganisationConsole, OrganisationConsole>();
    }

    static void AddShared(IServiceCollection services, string connectionString, string? blobConnectionString, string? blobEndpoint)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        // Azure sets the endpoint (managed identity); local runs a connection string. With neither, the logo store refuses every call (fail closed).
        services.TryAddSingleton<ILogoStore>(blobEndpoint is { Length: > 0 } ? new BlobLogoStore(new Uri(blobEndpoint)) : blobConnectionString is null ? new NoLogoStore() : new BlobLogoStore(blobConnectionString));
        services.AddDbContext<TenancyDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));
    }

    /// <summary>Needs the Audit module (flag changes and invitations are audited) and an IEmailSender (invitations).</summary>
    public static IServiceCollection AddTenancyModule(this IServiceCollection services, string connectionString, string? blobConnectionString = null, string? blobEndpoint = null)
    {
        AddShared(services, connectionString, blobConnectionString, blobEndpoint);
        return services
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
            .AddScoped<IOrganisationState, OrganisationStates>()
            .AddScoped<IOrganisationRequests, OrganisationRequests>()
            .AddScoped<IOrganisationReviews>(sp => new OrganisationReviews(connectionString, sp.GetRequiredService<ICurrentUser>()))
            .AddScoped<IStaffRoster, StaffRoster>()
            .AddScoped<IAccessReviews, AccessReviews>()
            .AddScoped<IComplianceFacts, ComplianceFactsReader>();
    }
}
