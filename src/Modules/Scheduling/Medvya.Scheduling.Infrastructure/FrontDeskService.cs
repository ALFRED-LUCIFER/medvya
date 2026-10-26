using System.Diagnostics.Metrics;
using System.Globalization;
using Medvya.Patients.Application;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// The reception board (MDV-P1-028). A move is a conditional update (only from the state the caller saw) plus its status
/// event, in one transaction: of two concurrent check-ins exactly one changes the row and writes the event, the other finds
/// the visit already checked in and returns it unchanged. After the commit the open boards of the facility are told (ids and
/// state only, MDV-P1-029) and refetch through the audited board endpoint. Check-in issues the visit's waiting-room token in the
/// same transaction (MDV-P1-031).
/// </summary>
public sealed partial class FrontDeskService(
    SchedulingDbContext db, SchedulingContext context, DoctorDayTransaction transaction, IBoardNotifier notifier, IOptions<SchedulingOptions> options,
    ILogger<FrontDeskService> log) : IFrontDeskService
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
        var tokens = await TokensAsync([.. visits.Select(a => a.Id)], cancellationToken);
        return new(facilityId, facility.TimeZone, date, now, [.. visits.Select(a => Card(a, patients, calendars, tokens))]);
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

    public async Task<ClinicOverview> GetOverviewAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanViewBoard, cancellationToken);
        var (facility, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var now = context.Now;
        var date = ScheduleRules.LocalDate(now, zone);
        var (from, to) = (ScheduleRules.Instant(date, TimeOnly.MinValue, zone), ScheduleRules.Instant(date.AddDays(1), TimeOnly.MinValue, zone));
        var visits = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == facilityId && a.CalendarKind == CalendarKind.Doctor && a.Start >= from && a.Start < to
                && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow)
            .OrderBy(a => a.Start).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        var patients = await context.PatientsAsync(visits.Select(a => a.PatientId), cancellationToken);
        var tokens = await TokensAsync([.. visits.Select(a => a.Id)], cancellationToken);
        var templates = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId && t.CalendarKind == CalendarKind.Doctor).ToListAsync(cancellationToken);
        var working = templates.Where(t => ScheduleRules.Day(t, date, zone).Hours.Count > 0).Select(t => t.CalendarId).ToHashSet();
        string Name(Guid patientId) => patients.GetValueOrDefault(patientId)?.FullName ?? "";

        // Every doctor with hours today or a visit today, in the day view's order.
        var doctors = (await context.CalendarsAsync(facilityId, cancellationToken))
            .Where(c => c.Kind == CalendarKind.Doctor && (working.Contains(c.Id) || visits.Exists(v => v.CalendarId == c.Id)))
            .Select(c =>
            {
                var own = visits.Where(v => v.CalendarId == c.Id).ToList();
                var counts = VisitFlow.BoardStates.ToDictionary(s => s, s => own.Count(v => v.State == s));
                var next = own.Where(v => v.State < AppointmentState.InConsult).Take(3)
                    .Select(v => new NextVisit(v.Id, v.Start, v.State, Name(v.PatientId), tokens.GetValueOrDefault(v.Id))).ToList();
                return new DoctorLoad(c.Id, c.Name, counts, ClinicDelay.Minutes(own.Select(v => (v.Start, v.State, v.ChangedAt)), now), next);
            }).ToList();
        var waiting = visits.Where(v => VisitFlow.CanReassign(v.State)).OrderBy(v => v.ChangedAt)
            .Select(v => new WaitingVisit(v.Id, tokens.GetValueOrDefault(v.Id), Name(v.PatientId), v.CalendarId, v.State, v.Start, v.ChangedAt)).ToList();
        return new(facilityId, facility.TimeZone, now, doctors, waiting);
    }

    public async Task<BoardCard> ReassignAsync(Guid appointmentId, ReassignVisit command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var seen = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        await context.RequireAsync(seen.FacilityId, SchedulingAccess.CanRunQueue, cancellationToken);
        var errors = new List<Application.ValidationError>();
        if (command.CalendarId is not { } target) errors.Add(new("calendarId", "required"));
        else if (target == seen.CalendarId) errors.Add(new("calendarId", "unchanged"));
        else if (!await context.IsActiveAsync(seen.FacilityId, target, CalendarKind.Doctor, cancellationToken)) errors.Add(new("calendarId", "unknown"));
        if (command.Reason is not { } reason || !Enum.IsDefined(reason)) errors.Add(new("reason", "required"));
        SchedulingValidationException.ThrowIfAny(errors);
        if (!VisitFlow.CanReassign(seen.State) || seen.CalendarKind != CalendarKind.Doctor) throw new VisitTransitionException("invalid_transition");
        var (_, zone) = await context.FacilityAsync(seen.FacilityId, cancellationToken);
        var (now, me, date) = (context.Now, context.UserId, ScheduleRules.LocalDate(seen.Start, zone));
        if (date != ScheduleRules.LocalDate(now, zone)) throw new VisitTransitionException("not_today");
        var to = command.CalendarId!.Value;

        // Both doctors' days are locked, so a booking or another move of either waits; the visit moves only from the queue and
        // state the desk saw. Its booked slot is freed: a waiting patient takes no slot in the new doctor's calendar.
        var moved = await transaction.RunAsync([(seen.CalendarId, date), (to, date)], async () =>
        {
            if (await db.Appointments.Where(a => a.Id == seen.Id && a.CalendarId == seen.CalendarId && a.State == seen.State)
                    .ExecuteUpdateAsync(u => u.SetProperty(a => a.CalendarId, to), cancellationToken) == 0)
                throw new VisitTransitionException("changed");
            await db.ScheduleSlots.Where(x => x.AppointmentId == seen.Id).ExecuteDeleteAsync(cancellationToken);
            db.Reschedules.Add(new AppointmentReschedule
            {
                Id = Guid.CreateVersion7(), TenantId = seen.TenantId, AppointmentId = seen.Id, FromCalendarId = seen.CalendarId, FromStart = seen.Start, FromEnd = seen.End,
                ToCalendarId = to, ToStart = seen.Start, ToEnd = seen.End, ActorUserId = me, OccurredAt = now, ReassignReason = command.Reason,
            });
            db.Outbox.Add(new OutboxEvent
            {
                Id = Guid.CreateVersion7(), TenantId = seen.TenantId, Type = "AppointmentReassigned.v1", AggregateId = seen.Id, OccurredAt = now,
                Payload = System.Text.Json.JsonSerializer.Serialize(new { appointmentId = seen.Id, facilityId = seen.FacilityId, fromCalendarId = seen.CalendarId, calendarId = to, reason = command.Reason }),
            });
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.Reassign, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, seen.Id, seen.PatientId, cancellationToken);
            seen.CalendarId = to;
            return seen;
        }, cancellationToken);
        await NotifyAsync(notifier, log, new(moved.TenantId, moved.FacilityId, moved.Id, moved.State, now));
        return await CardAsync(moved, cancellationToken);
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
        if (to == AppointmentState.CheckedIn) await IssueTokenAsync(visit, ScheduleRules.LocalDate(now, zone), now, cancellationToken);
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

    /// <summary>
    /// The next token of the visit's calendar for the facility's day ("A-07"), added to the open check-in transaction. A per
    /// facility-day app lock serialises issuing, so two desks never hand out the same number; the unique index is the backstop.
    /// A calendar keeps its letter all day; a calendar new to the day takes the next free letter.
    /// </summary>
    async Task IssueTokenAsync(Appointment visit, DateOnly date, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await DoctorDayTransaction.LockAsync(db, string.Create(CultureInfo.InvariantCulture, $"queue:{visit.FacilityId}:{date:yyyy-MM-dd}"),
                options.Value.LockTimeout, cancellationToken);
        }
        catch (DoctorDayTransaction.LockNotGrantedException)
        {
            throw new VisitTransitionException("busy"); // rolls back the check-in; the desk tries again
        }
        var today = db.QueueTokens.Where(t => t.FacilityId == visit.FacilityId && t.Date == date);
        var prefix = await today.Where(t => t.CalendarId == visit.CalendarId).Select(t => t.Prefix).FirstOrDefaultAsync(cancellationToken)
            ?? QueueToken.PrefixAt(await today.Select(t => t.CalendarId).Distinct().CountAsync(cancellationToken));
        var number = (await today.Where(t => t.Prefix == prefix).MaxAsync(t => (int?)t.Number, cancellationToken) ?? 0) + 1;
        db.QueueTokens.Add(new QueueToken
        {
            Id = Guid.CreateVersion7(), TenantId = visit.TenantId, FacilityId = visit.FacilityId, Date = date, CalendarId = visit.CalendarId,
            Prefix = prefix, Number = number, AppointmentId = visit.Id, IssuedAt = now,
        });
    }

    /// <summary>The tokens of these visits ("A-07"), by appointment id.</summary>
    async Task<Dictionary<Guid, string>> TokensAsync(Guid[] appointmentIds, CancellationToken cancellationToken) =>
        (await db.QueueTokens.AsNoTracking().Where(t => appointmentIds.Contains(t.AppointmentId))
            .Select(t => new { t.AppointmentId, t.Prefix, t.Number }).ToListAsync(cancellationToken))
        .ToDictionary(t => t.AppointmentId, t => QueueToken.Format(t.Prefix, t.Number));

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
        return Card(visit, patients, calendars, await TokensAsync([visit.Id], cancellationToken));
    }

    static BoardCard Card(Appointment a, IReadOnlyDictionary<Guid, PatientSummary> patients, Dictionary<Guid, string> calendars, Dictionary<Guid, string> tokens)
    {
        var p = patients.GetValueOrDefault(a.PatientId);
        return new(a.Id, AppointmentReference.Of(a.Id), a.PatientId, p?.FullName ?? "", p?.DateOfBirth, p?.RegistrationIncomplete ?? false,
            a.CalendarId, calendars.GetValueOrDefault(a.CalendarId, ""), a.VisitType, a.ReasonCategory, a.Start, a.End, a.State, a.ChangedAt, a.Room, tokens.GetValueOrDefault(a.Id));
    }
}
