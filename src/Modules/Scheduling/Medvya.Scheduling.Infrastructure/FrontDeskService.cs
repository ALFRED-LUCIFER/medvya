using System.Diagnostics.Metrics;
using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// The reception board (MDV-P1-028). A move is a conditional update (only from the state the caller saw) plus its status
/// event, in one transaction: of two concurrent check-ins exactly one changes the row and writes the event, the other finds
/// the visit already checked in and returns it unchanged. After the commit the open boards of the facility are told (ids and
/// state only, MDV-P1-029) and refetch through the audited board endpoint.
/// </summary>
public sealed partial class FrontDeskService(SchedulingDbContext db, SchedulingContext context, IBoardNotifier notifier, ILogger<FrontDeskService> log) : IFrontDeskService
{
    /// <summary>Meter of front-desk measurements. Values are durations only; the one tag is the opaque tenant id.</summary>
    public const string MeterName = "Medvya.FrontDesk";
    static readonly Meter Meter = new(MeterName);
    static readonly Histogram<double> CheckInSeconds = Meter.CreateHistogram<double>(
        "medvya.front_desk.check_in.duration", "s", "From tapping Check in to the visit being checked in (exit gate 3).");
    // A check-in left open longer than this is someone walking away from the desk, not a check-in time.
    static readonly TimeSpan MaxCheckIn = TimeSpan.FromMinutes(30);

    public async Task<FrontDeskBoard> GetBoardAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanViewBoard, cancellationToken);
        var (facility, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var now = context.Now;
        var date = ScheduleRules.LocalDate(now, zone);
        var (from, to) = (ScheduleRules.Instant(date, TimeOnly.MinValue, zone), ScheduleRules.Instant(date.AddDays(1), TimeOnly.MinValue, zone));

        var visits = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == facilityId && a.Start >= from && a.Start < to && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow)
            .OrderBy(a => a.Start).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        var patients = await context.PatientsAsync(visits.Select(a => a.PatientId), cancellationToken);
        var calendars = (await context.CalendarsAsync(facilityId, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        return new(facilityId, facility.TimeZone, date, now, [.. visits.Select(a => Card(a, patients, calendars))]);
    }

    public async Task<BoardCard> CheckInAsync(Guid appointmentId, CheckIn command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (visit, moved) = await MoveAsync(appointmentId, AppointmentState.CheckedIn, SchedulingAudit.CheckIn, cancellationToken);
        if (moved && command.ElapsedMs is long ms && ms > 0 && TimeSpan.FromMilliseconds(ms) <= MaxCheckIn)
            CheckInSeconds.Record(ms / 1000d, new KeyValuePair<string, object?>("tenant_id", visit.TenantId));
        return await CardAsync(visit, cancellationToken);
    }

    public async Task<BoardCard> MoveAsync(Guid appointmentId, MoveVisit command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.State is not { } to || !Enum.IsDefined(to)) throw new SchedulingValidationException([new("state", "required")]);
        var (visit, _) = await MoveAsync(appointmentId, to, SchedulingAudit.Status, cancellationToken);
        return await CardAsync(visit, cancellationToken);
    }

    public async Task<string> WatchGroupAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanViewBoard, cancellationToken);
        return BoardGroups.Of(context.TenantId, facilityId);
    }

    /// <summary>
    /// Moves the visit to <paramref name="to"/>; Moved is false when it was already there (an idempotent repeat). Either way the
    /// attempt is audited as <paramref name="action"/> in the transaction of what it wrote.
    /// </summary>
    async Task<(Appointment Visit, bool Moved)> MoveAsync(Guid appointmentId, AppointmentState to, string action, CancellationToken cancellationToken)
    {
        var visit = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        await context.RequireAsync(visit.FacilityId, SchedulingAccess.CanRunQueue, cancellationToken);
        if (visit.State == to) return (await RepeatAsync(visit, action, cancellationToken), false);
        Guard(visit.State, to);
        var (_, zone) = await context.FacilityAsync(visit.FacilityId, cancellationToken);
        var now = context.Now;
        if (to == AppointmentState.CheckedIn && ScheduleRules.LocalDate(visit.Start, zone) != ScheduleRules.LocalDate(now, zone))
            throw new VisitTransitionException("not_today");

        var (from, me) = (visit.State, context.UserId);
        // Call-in takes the room of the doctor's session now (or at the visit's time) from the schedule template.
        var room = to == AppointmentState.InConsult ? await RoomAsync(visit, zone, now, cancellationToken) : visit.Room;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = await db.Appointments.Where(a => a.Id == appointmentId && a.State == from)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, to).SetProperty(a => a.Room, room)
                .SetProperty(a => a.ChangedBy, me).SetProperty(a => a.ChangedAt, now), cancellationToken);
        if (changed == 0)
        {
            // Someone else moved it first: the same move is a repeat, anything else conflicts.
            await tx.RollbackAsync(cancellationToken);
            var current = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId, cancellationToken);
            if (current.State == to) return (await RepeatAsync(current, action, cancellationToken), false);
            Guard(current.State, to);
            throw new VisitTransitionException("invalid_transition");
        }
        db.StatusEvents.Add(new VisitStatusEvent
        {
            Id = Guid.CreateVersion7(), TenantId = visit.TenantId, AppointmentId = visit.Id, State = to, ActorUserId = me, OccurredAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await context.AuditAsync(action, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, visit.Id, visit.PatientId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        visit.State = to;
        visit.Room = room;
        visit.ChangedBy = me;
        visit.ChangedAt = now;
        await NotifyAsync(notifier, log, new(visit.TenantId, visit.FacilityId, visit.Id, to, now));
        return (visit, true);
    }

    /// <summary>
    /// Tells open boards after the commit. A failed push loses nothing (boards refetch when they rejoin and poll while not
    /// live), so it is logged with the facility id only and the committed change still succeeds.
    /// </summary>
    internal static async Task NotifyAsync(IBoardNotifier notifier, ILogger log, BoardChange change)
    {
        try
        {
            await notifier.ChangedAsync(change, CancellationToken.None);
        }
        catch (Exception e) // any push failure is non-fatal by design (see summary); telemetry keeps only the exception type
        {
            BoardPushFailed(log, e, change.FacilityId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Board update for facility {FacilityId} not pushed")]
    static partial void BoardPushFailed(ILogger logger, Exception exception, Guid facilityId);

    /// <summary>The room of the calendar's session running now, else of the session the visit was booked in; else unchanged.</summary>
    async Task<string?> RoomAsync(Appointment visit, TimeZoneInfo zone, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var template = await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.FacilityId == visit.FacilityId && t.CalendarId == visit.CalendarId, cancellationToken);
        if (template is null) return visit.Room;
        var hours = ScheduleRules.Day(template, ScheduleRules.LocalDate(now, zone), zone).Hours;
        var session = hours.FirstOrDefault(w => w.Start <= now && now < w.End) ?? hours.FirstOrDefault(w => w.Start <= visit.Start && visit.Start < w.End);
        return session?.Room ?? visit.Room;
    }

    /// <summary>A repeated move changes nothing; the attempt is still audited.</summary>
    async Task<Appointment> RepeatAsync(Appointment visit, string action, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await context.AuditAsync(action, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, visit.Id, visit.PatientId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return visit;
    }

    static void Guard(AppointmentState from, AppointmentState to)
    {
        if (!VisitFlow.CanMove(from, to)) throw new VisitTransitionException("invalid_transition");
    }

    async Task<BoardCard> CardAsync(Appointment visit, CancellationToken cancellationToken)
    {
        var patients = await context.PatientsAsync([visit.PatientId], cancellationToken);
        var calendars = (await context.CalendarsAsync(visit.FacilityId, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        return Card(visit, patients, calendars);
    }

    static BoardCard Card(Appointment a, IReadOnlyDictionary<Guid, PatientSummary> patients, Dictionary<Guid, string> calendars)
    {
        var p = patients.GetValueOrDefault(a.PatientId);
        return new(a.Id, AppointmentReference.Of(a.Id), a.PatientId, p?.FullName ?? "", p?.DateOfBirth, p?.RegistrationIncomplete ?? false,
            a.CalendarId, calendars.GetValueOrDefault(a.CalendarId, ""), a.VisitType, a.ReasonCategory, a.Start, a.End, a.State, a.ChangedAt, a.Room, a.WalkIn, a.Unscheduled);
    }
}
