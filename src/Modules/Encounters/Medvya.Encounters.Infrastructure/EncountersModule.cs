using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Encounters.Infrastructure;

public static class EncountersModule
{
    /// <summary>Needs Tenancy (clinical access), Scheduling (the visit), Patients (merges) and Audit.</summary>
    public static IServiceCollection AddEncountersModule(this IServiceCollection services, string connectionString)
    {
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<EncountersDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IEncounters, EncounterService>()
            .AddScoped<IPatientChart, PatientChartService>()
            .AddScoped<IPatientTimeline, PatientTimelineService>()
            .AddScoped<Medvya.Tenancy.Application.ICareRelationshipSource, EncounterCareRelationship>()
            .AddScoped<Medvya.Patients.Application.IPatientMergeParticipant, EncounterMergeParticipant>();
    }
}
