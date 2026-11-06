using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Medvya.Patients.Application;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Moves a merged patient's visits to the surviving record and back (MDV-P1-017), inside the merge's transaction on its connection
/// (so the tenant's row-level security applies and the move commits or rolls back with the merge).
/// </summary>
public sealed class PatientMergeParticipant(SchedulingDbContext db, ICurrentTenant tenant) : IPatientMergeParticipant
{
    // shortcut: no clinical records exist before P2, so no visit counts as clinical data; the chart module reports it from P2 (MDV-P2-*).
    public Task<bool> HasClinicalDataAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<int> VisitsAsync(Guid patientId, CancellationToken cancellationToken) =>
        db.Appointments.AsNoTracking().CountAsync(a => a.PatientId == patientId, cancellationToken);

    public async Task MoveAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        await shared.Appointments.Where(a => a.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PatientId, toPatientId).SetProperty(a => a.MergedFromPatientId, fromPatientId), cancellationToken);
    }

    public async Task RestoreAsync(Guid fromPatientId, Guid toPatientId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        await shared.Appointments.Where(a => a.PatientId == toPatientId && a.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PatientId, fromPatientId).SetProperty(a => a.MergedFromPatientId, (Guid?)null), cancellationToken);
    }

    async Task<SchedulingDbContext> SharedAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        var shared = new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        return shared;
    }
}
