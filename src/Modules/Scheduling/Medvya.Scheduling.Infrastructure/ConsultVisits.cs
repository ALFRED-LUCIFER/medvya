using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// The visit side of a consultation (MDV-P2-001). Moves are conditional updates from the state read, with their status event, on the
/// encounter's connection and transaction (so row-level security applies and they commit with the encounter). The board is told after the
/// commit, by ids and state only, exactly as for a move at the desk (MDV-P1-029). Authorisation is the encounter use case's (clinical access).
/// </summary>
public sealed class ConsultVisits(SchedulingDbContext db, SchedulingContext context, ICurrentTenant tenant, IBoardNotifier notifier, ILogger<ConsultVisits> log) : IConsultVisits
{
    public Task<ConsultVisit?> FindAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId)
            .Select(a => new ConsultVisit(a.Id, a.FacilityId, a.PatientId, a.State, a.Room, a.Start)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ConsultVisit> StartAsync(Guid appointmentId, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        var visit = await shared.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken) ?? throw new AppointmentNotFoundException();
        if (visit.State == AppointmentState.InConsult) return View(visit);
        if (!VisitFlow.CanStartConsult(visit.State))
            throw new VisitTransitionException(visit.State is AppointmentState.Booked or AppointmentState.NeedsRebooking ? "not_arrived" : "finished");
        var (_, zone) = await context.FacilityAsync(visit.FacilityId, cancellationToken);
        var now = context.Now;
        if (ScheduleRules.LocalDate(visit.Start, zone) != ScheduleRules.LocalDate(now, zone)) throw new VisitTransitionException("not_today");
        var room = await context.RoomAsync(visit, zone, now, cancellationToken);
        return await MoveAsync(shared, visit, AppointmentState.InConsult, room, now, cancellationToken);
    }

    public async Task<ConsultVisit> FinishAsync(Guid appointmentId, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        var visit = await shared.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken) ?? throw new AppointmentNotFoundException();
        // The desk may already have moved it on (or back to the queue): the consultation still ends, the visit stays where it is.
        // With billing off the visit is done when the consultation ends (MDV audit UX-05).
        return visit.State == AppointmentState.InConsult
            ? await MoveAsync(shared, visit, VisitFlow.Next(visit.State, await context.StepsAsync(cancellationToken))!.Value, visit.Room, context.Now, cancellationToken)
            : View(visit);
    }

    public async Task<ConsultVisit> HandOffAsync(Guid appointmentId, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        var visit = await shared.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken) ?? throw new AppointmentNotFoundException();
        if (visit.State == AppointmentState.ReadyForDoctor) return View(visit);
        if (visit.State != AppointmentState.WithNurse) throw new VisitTransitionException("invalid_transition");
        return await MoveAsync(shared, visit, AppointmentState.ReadyForDoctor, visit.Room, context.Now, cancellationToken);
    }

    public Task NotifyAsync(ConsultVisit visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visit);
        return FrontDeskService.NotifyAsync(notifier, log, new BoardChange(context.TenantId, visit.FacilityId, visit.Id, visit.State, context.Now));
    }

    async Task<ConsultVisit> MoveAsync(SchedulingDbContext shared, Appointment visit, AppointmentState to, string? room, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var me = context.UserId;
        var changed = await shared.Appointments.Where(a => a.Id == visit.Id && a.State == visit.State)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, to).SetProperty(a => a.Room, room).SetProperty(a => a.ChangedBy, me).SetProperty(a => a.ChangedAt, now), cancellationToken);
        if (changed == 0)
        {
            // Moved by someone else since it was read: already where this move goes is fine, anything else is a conflict to retry.
            var current = await shared.Appointments.AsNoTracking().SingleAsync(a => a.Id == visit.Id, cancellationToken);
            return current.State == to ? View(current) : throw new VisitTransitionException("invalid_transition");
        }
        shared.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = visit.TenantId, AppointmentId = visit.Id, State = to, ActorUserId = me, OccurredAt = now });
        await shared.SaveChangesAsync(cancellationToken);
        return new ConsultVisit(visit.Id, visit.FacilityId, visit.PatientId, to, room, visit.Start);
    }

    async Task<SchedulingDbContext> SharedAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var shared = new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        return shared;
    }

    static ConsultVisit View(Appointment a) => new(a.Id, a.FacilityId, a.PatientId, a.State, a.Room, a.Start);
}
