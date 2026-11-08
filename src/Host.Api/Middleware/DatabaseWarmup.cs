using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Host.Api.Middleware;

/// <summary>
/// Pays EF Core's first-use cost (model building, query compilation, JIT of EF and Npgsql) at startup, not inside the first request of each
/// kind: on App Service B1 the first search, board and schedule after a restart took 7-9 s (#912). In the background, like
/// <see cref="Medvya.BuildingBlocks.Security.KeyVaultWarmup"/>, so startup and /health do not wait. Reads and writes nothing: the scope has
/// no tenant (EF filters and row-level security match no row) and every query is LIMIT 0.
/// </summary>
public sealed partial class DatabaseWarmup(IServiceProvider services, IReadOnlyList<Type> contexts, ILogger<DatabaseWarmup> log) : IHostedService
{
    static readonly MethodInfo SetOf = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;

    /// <summary>The warm-up in flight.</summary>
    public Task? Running { get; private set; }

    /// <summary>Warms every DbContext registered so far: call it after the modules are added.</summary>
    public static void AddTo(IServiceCollection services)
    {
        Type[] contexts = [.. services.Select(d => d.ServiceType).Where(t => t.IsSubclassOf(typeof(DbContext))).Distinct()];
        services.AddHostedService(sp => new DatabaseWarmup(sp, contexts, sp.GetRequiredService<ILogger<DatabaseWarmup>>()));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Running = Task.Run(WarmAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    async Task WarmAsync()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            foreach (var type in contexts)
            {
                await using var scope = services.CreateAsyncScope();
                var db = (DbContext)scope.ServiceProvider.GetRequiredService(type);
                foreach (var entity in db.Model.GetEntityTypes().Where(e => !e.IsOwned() && !e.HasSharedClrType && e.GetTableName() is not null))
                    await ((IQueryable<object>)SetOf.MakeGenericMethod(entity.ClrType).Invoke(db, null)!).AsNoTracking().Take(0).ToListAsync();
            }
            Finished(log, contexts.Count, clock.ElapsedMilliseconds);
        }
        // E.g. the database is briefly unavailable: the first requests then pay the cost, as before. The type only (EF wraps a transient
        // NpgsqlException in an InvalidOperationException); exception text can carry SQL or values.
        catch (Exception e) { Failed(log, (e.InnerException ?? e).GetType().Name); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Count and ElapsedMilliseconds are names the telemetry scrubber keeps, so the time shows in Application Insights.
    [LoggerMessage(Level = LogLevel.Information, Message = "Database warm-up finished: {Count} contexts in {ElapsedMilliseconds} ms.")]
    static partial void Finished(ILogger logger, int count, long elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database warm-up failed: {ErrorType}. The first requests after this start will be slow.")]
    static partial void Failed(ILogger logger, string errorType);
}
