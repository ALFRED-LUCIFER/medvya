using Azure.Identity;
using Azure.Storage.Blobs;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Medvya.Encounters.Infrastructure;

public static class EncountersModule
{
    /// <summary>Needs Tenancy (clinical access, feature flags), Scheduling (the visit), Patients (merges) and Audit.</summary>
    public static IServiceCollection AddEncountersModule(this IServiceCollection services, string connectionString, IConfiguration? configuration = null, bool development = false)
    {
        AddDocumentStorage(services, configuration, development);
        services.TryAddScoped<TenantSessionInterceptor>();
        services.TryAddSingleton(TimeProvider.System);
        return services
            .AddDbContext<EncountersDbContext>((sp, o) => o.UseNpgsql(PostgresDataSources.For(connectionString)).AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()))
            .AddScoped<IEncounters, EncounterService>()
            .AddScoped<IEncounterDirectory, EncounterDirectory>()
            .AddScoped<IDocumentDirectory, DocumentDirectory>()
            .AddScoped<IPatientChart, PatientChartService>()
            .AddScoped<IPatientTimeline, PatientTimelineService>()
            .AddScoped<IDocuments, DocumentService>()
            .AddScoped<IIntake, IntakeService>()
            .AddScoped<IMedicationReconciliation, MedicationReconciliationService>()
            .AddScoped<IReconciliationStops, ReconciliationStops>()
            .AddScoped<IProblemList, ProblemListService>()
            .AddScoped<IPrescribingChart, PrescribingChart>()
            .AddScoped<INoteTemplates, NoteTemplateService>()
            .AddScoped<IConsultNotes, ConsultNoteService>()
            .AddScoped<Medvya.Scheduling.Application.IHandoffGate, IntakeHandoffGate>()
            .AddScoped<Medvya.Tenancy.Application.ICareRelationshipSource, EncounterCareRelationship>()
            .AddScoped<Medvya.Patients.Application.IPatientMergeParticipant, EncounterMergeParticipant>();
    }

    /// <summary>
    /// Documents (MDV-P2-006): Storage:BlobEndpoint is Azure with the managed identity (user-delegation links, Defender for Storage verdicts);
    /// Storage:ConnectionString is local Azurite, Development only (account-key links, the local EICAR stand-in scanner). Anything else: documents fail closed.
    /// </summary>
    static void AddDocumentStorage(IServiceCollection services, IConfiguration? configuration, bool development)
    {
        if (configuration?["Storage:BlobEndpoint"] is { Length: > 0 } endpoint)
        {
            var store = new BlobDocumentStore(new BlobServiceClient(new Uri(endpoint), new DefaultAzureCredential()), userDelegation: true, createContainers: false);
            services.TryAddSingleton<IDocumentStore>(store);
            services.TryAddSingleton<IMalwareScan>(new DefenderMalwareScan(store));
        }
        // The local stand-in scanner is not a scanner: a connection string outside Development must not make every file "clean".
        else if (development && configuration?["Storage:ConnectionString"] is { Length: > 0 } local)
        {
            var store = new BlobDocumentStore(new BlobServiceClient(local), userDelegation: false, createContainers: true);
            services.TryAddSingleton<IDocumentStore>(store);
            services.TryAddSingleton<IMalwareScan>(new LocalEicarScan(store));
        }
        else
        {
            services.TryAddSingleton<IDocumentStore, NoDocumentStore>();
            services.TryAddSingleton<IMalwareScan, NoDocumentStore>();
        }
    }
}
