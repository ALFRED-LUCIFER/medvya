using System.Text.Json;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Leave and one-off exceptions (MDV-P1-012). Saving runs on the booking path: every doctor-day of the range is locked, the booked
/// visits it leaves without a doctor are found inside the lock, and each must have an action or nothing is saved. A moved visit
/// becomes "needs rebooking" (its slots are freed, so nobody is booked into the gap), with a status event, an audit event and, for
/// "offer", AppointmentNeedsRebooking.v1 that makes the patient's rebooking message. All in one transaction with the exception.
/// shortcut: only Booked visits are affected (a visit already checked in is in progress); a dedicated rebooking list and link
/// for the patient come with the patient portal (P2).
/// </summary>
public sealed class ScheduleExceptionService(
    SchedulingDbContext db, SchedulingContext context, DoctorDayTransaction transaction, IBoardNotifier notifier, ILogger<ScheduleExceptionService> log) : IScheduleExceptionService
{
    public async Task<IReadOnlyList<ExceptionView>> ListAsync(Guid facilityId, Guid? calendarId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanView, cancellationToken);
        var (_, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var today = ScheduleRules.LocalDate(context.Now, zone);
        var rows = await context.ExceptionsAsync(facilityId, today, DateOnly.MaxValue, calendarId, cancellationToken);
        return [.. rows.OrderBy(x => x.FromDate).ThenBy(x => x.Start).Select(View)];
    }

    public async Task<SavedAdjustment> SaveAsync(SaveAdjustment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        var calendar = (await context.CalendarsAsync(command.FacilityId, cancellationToken)).SingleOrDefault(c => c.Id == command.CalendarId)
            ?? throw new SchedulingValidationException([new("calendarId", "unknown")]);
        var (_, zone) = await context.FacilityAsync(command.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId);
        var template = await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.FacilityId == command.FacilityId && t.CalendarId == calendar.Id, cancellationToken);

        var exception = new ScheduleAdjustment
        {
            Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, CalendarId = calendar.Id, Kind = command.Kind,
            FromDate = command.FromDate, ToDate = command.ToDate, Start = command.Start, End = command.End, Reason = ScheduleService.Clean(command.Reason), CreatedBy = me, CreatedAt = now,
        };
        ScheduleRules.Validate(exception, template);
        var errors = new List<ValidationError>();
        if (exception.FromDate < ScheduleRules.LocalDate(now, zone)) errors.Add(new("fromDate", "past"));
        if (exception.Kind != ExceptionKind.Leave && template is null) errors.Add(new("calendarId", "no_schedule")); // nothing to extend or shorten
        SchedulingValidationException.ThrowIfAny(errors);

        var days = Enumerable.Range(0, exception.ToDate.DayNumber - exception.FromDate.DayNumber + 1).Select(i => exception.FromDate.AddDays(i)).ToList();
        var (offered, manual, moved) = await transaction.RunAsync(days.Select(d => (calendar.Id, d)), async () =>
        {
            var existing = await db.Exceptions.AsNoTracking()
                .Where(x => x.FacilityId == exception.FacilityId && x.CalendarId == calendar.Id && x.FromDate <= exception.ToDate && x.ToDate >= exception.FromDate).ToListAsync(cancellationToken);
            if (exception.Kind != ExceptionKind.Leave && existing.Exists(x => x.Kind == exception.Kind
                    && (exception.Kind == ExceptionKind.ShortenedDay || (x.Start < exception.End && exception.Start < x.End))))
                throw new SchedulingValidationException([new("start", "overlap")]);

            var affected = await AffectedAsync(exception, template, [.. existing, exception], zone, cancellationToken);
            var byId = (command.Actions ?? []).GroupBy(a => a.AppointmentId).ToDictionary(g => g.Key, g => g.First().Action);
            if (byId.Keys.Any(id => affected.TrueForAll(a => a.Id != id)) || (command.Actions ?? []).Any(a => !Enum.IsDefined(a.Action)))
                throw new SchedulingValidationException([new("actions", "unknown")]);
            if (affected.Exists(a => !byId.ContainsKey(a.Id)))
            {
                var names = await context.PatientsAsync(affected.Select(a => a.PatientId), cancellationToken);
                throw new ExceptionActionsRequiredException([.. affected.Select(a => SchedulingContext.Chip(a, names))]);
            }

            db.Exceptions.Add(exception);
            foreach (var a in affected)
            {
                if (await db.Appointments.Where(x => x.Id == a.Id && x.State == AppointmentState.Booked && x.Start == a.Start)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, AppointmentState.NeedsRebooking).SetProperty(x => x.ChangedBy, me).SetProperty(x => x.ChangedAt, now), cancellationToken) == 0)
                    throw new VisitTransitionException("changed"); // checked in or moved by someone else since the list was read
                await db.ScheduleSlots.Where(s => s.AppointmentId == a.Id).ExecuteDeleteAsync(cancellationToken); // the gap cannot be booked into
                db.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = a.TenantId, AppointmentId = a.Id, State = AppointmentState.NeedsRebooking, ActorUserId = me, OccurredAt = now });
                if (byId[a.Id] == RebookAction.Offer)
                    db.Outbox.Add(new OutboxEvent
                    {
                        Id = Guid.CreateVersion7(), TenantId = a.TenantId, Type = "AppointmentNeedsRebooking.v1", AggregateId = a.Id, OccurredAt = now,
                        Payload = JsonSerializer.Serialize(new { appointmentId = a.Id, facilityId = a.FacilityId, calendarId = a.CalendarId, patientId = a.PatientId, start = a.Start }),
                    });
            }
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.ExceptionCreate, SchedulingAudit.ExceptionResource, SchedulingAudit.Operations, exception.Id, null, cancellationToken);
            foreach (var a in affected)
                await context.AuditAsync(SchedulingAudit.NeedsRebooking, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, a.Id, a.PatientId, cancellationToken);
            return (byId.Count(p => p.Value == RebookAction.Offer), byId.Count(p => p.Value == RebookAction.Manual), affected);
        }, cancellationToken);

        foreach (var a in moved) // the cards appear in the board's "needs rebooking" lane
            await FrontDeskService.NotifyAsync(notifier, log, new(a.TenantId, a.FacilityId, a.Id, AppointmentState.NeedsRebooking, now));
        return new(View(exception), offered, manual);
    }

    public async Task<int> ReleaseDoctorAsync(Guid doctorUserId, CancellationToken cancellationToken = default)
    {
        var (now, me) = (context.Now, context.UserId);
        var zones = new Dictionary<Guid, TimeZoneInfo>();
        async Task<List<Appointment>> FutureAsync() => await db.Appointments.AsNoTracking()
            .Where(a => a.CalendarId == doctorUserId && a.Start >= now && a.State == AppointmentState.Booked && !a.Unscheduled).ToListAsync(cancellationToken);
        var days = new List<(Guid, DateOnly)>();
        foreach (var a in await FutureAsync())
        {
            if (!zones.TryGetValue(a.FacilityId, out var zone)) zones[a.FacilityId] = zone = (await context.FacilityAsync(a.FacilityId, cancellationToken)).Zone;
            days.Add((doctorUserId, ScheduleRules.LocalDate(a.Start, zone)));
        }
        var moved = await transaction.RunAsync(days, async () =>
        {
            var affected = await FutureAsync();
            foreach (var a in affected)
            {
                if (await db.Appointments.Where(x => x.Id == a.Id && x.State == AppointmentState.Booked)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, AppointmentState.NeedsRebooking).SetProperty(x => x.ChangedBy, me).SetProperty(x => x.ChangedAt, now), cancellationToken) == 0)
                    throw new VisitTransitionException("changed");
                await db.ScheduleSlots.Where(s => s.AppointmentId == a.Id).ExecuteDeleteAsync(cancellationToken);
                db.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = a.TenantId, AppointmentId = a.Id, State = AppointmentState.NeedsRebooking, ActorUserId = me, OccurredAt = now });
            }
            await db.SaveChangesAsync(cancellationToken);
            foreach (var a in affected)
                await context.AuditAsync(SchedulingAudit.NeedsRebooking, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, a.Id, a.PatientId, cancellationToken);
            return affected;
        }, cancellationToken);
        foreach (var a in moved) await FrontDeskService.NotifyAsync(notifier, log, new(a.TenantId, a.FacilityId, a.Id, AppointmentState.NeedsRebooking, now));
        return moved.Count;
    }

    /// <summary>Booked visits of the calendar in the range that the exception leaves outside its working hours (all of them, for leave).</summary>
    async Task<List<Appointment>> AffectedAsync(ScheduleAdjustment x, ScheduleTemplate? template, List<ScheduleAdjustment> all, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (x.Kind == ExceptionKind.ExtraSession) return [];
        var from = ScheduleRules.DayBounds(x.FromDate, zone).From;
        var to = ScheduleRules.DayBounds(x.ToDate, zone).To;
        var booked = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == x.FacilityId && a.CalendarId == x.CalendarId && a.Start >= from && a.Start < to && a.State == AppointmentState.Booked && !a.Unscheduled)
            .OrderBy(a => a.Start).ToListAsync(cancellationToken);
        return x.Kind == ExceptionKind.Leave || template is null
            ? booked
            : [.. booked.Where(a => !ScheduleRules.Covers(ScheduleRules.Day(template, ScheduleRules.LocalDate(a.Start, zone), zone, all), a.Start, a.End))];
    }

    static ExceptionView View(ScheduleAdjustment x) => new(x.Id, x.CalendarId, x.Kind, x.FromDate, x.ToDate, x.Start, x.End, x.Reason);
}
