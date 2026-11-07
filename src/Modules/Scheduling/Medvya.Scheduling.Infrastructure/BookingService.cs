using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Slot holds and booking (MDV-P1-021), reschedule, cancel and no-show (MDV-P1-024). Every write that takes or frees slots runs
/// in a <see cref="DoctorDayTransaction"/> (MDV-P1-022) and re-checks that the slots are still slots of the template, free of
/// appointments and not held by someone else. Each change, its history and its outbox event are written in one transaction,
/// with the attempt's audit event (MDV-P0-017), which carries the patient id.
/// </summary>
public sealed class BookingService(
    SchedulingDbContext db, SchedulingContext context, DoctorDayTransaction transaction, IOptions<SchedulingOptions> options, IBoardNotifier notifier, ILogger<BookingService> log)
    : IBookingService
{
    const int MaxReason = 500;

    public async Task<HoldView> HoldAsync(HoldSlot command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        if (!Enum.IsDefined(command.VisitType)) throw new SchedulingValidationException([new("visitType", "invalid")]);
        var (_, zone) = await context.FacilityAsync(command.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId!.Value); // RequireAsync found this user's membership
        if (command.Start < now) throw new SlotUnavailableException("in_past");

        return await transaction.RunAsync([(command.CalendarId, ScheduleRules.LocalDate(command.Start, zone))], async () =>
        {
            var end = await BookableEndAsync(command.FacilityId, command.CalendarId, command.Start, command.VisitType, zone, cancellationToken);
            if (await db.SlotHolds.AnyAsync(h => h.CalendarId == command.CalendarId && h.FacilityId == command.FacilityId && h.Start < end && command.Start < h.End
                    && h.ExpiresAt > now && h.HeldBy != me, cancellationToken))
                throw new SlotUnavailableException("being_booked");

            // One hold per user (picking another slot or visit type replaces it); expired holds of this calendar go as well.
            await db.SlotHolds.Where(h => (h.HeldBy == me && h.FacilityId == command.FacilityId) || (h.CalendarId == command.CalendarId && h.ExpiresAt <= now))
                .ExecuteDeleteAsync(cancellationToken);
            var hold = new SlotHold
            {
                Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, CalendarId = command.CalendarId,
                VisitType = command.VisitType, Start = command.Start, End = end, HeldBy = me, ExpiresAt = now + options.Value.SlotHold,
            };
            db.SlotHolds.Add(hold);
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.SlotHold, SchedulingAudit.HoldResource, SchedulingAudit.Treatment, hold.Id, null, cancellationToken);
            return new HoldView(hold.Id, hold.FacilityId, hold.CalendarId, hold.VisitType, hold.Start, hold.End, hold.ExpiresAt);
        }, cancellationToken);
    }

    public async Task ReleaseHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var me = context.UserId;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SlotHolds.Where(h => h.Id == holdId && h.HeldBy == me).ExecuteDeleteAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.SlotRelease, SchedulingAudit.HoldResource, SchedulingAudit.Treatment, holdId, null, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<AppointmentView> BookAsync(BookAppointment command, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var key = IdempotencyHashes.Of(idempotencyKey, command);
        // A repeat comes first: its hold is already gone, so the checks below would answer "hold_expired" instead of the first outcome.
        if (key is not null && await ReplayAsync(key, cancellationToken) is { } first) return first;
        var hold = await db.SlotHolds.AsNoTracking().SingleOrDefaultAsync(h => h.Id == command.HoldId, cancellationToken);
        // An identical request that raced this one may have booked and deleted the hold since the replay check above: answer with its outcome.
        if (hold is null && key is not null && await ReplayAsync(key, cancellationToken) is { } raced) return raced;
        if (hold is null) throw new SlotUnavailableException("hold_expired"); // released, replaced or cleaned up after expiry
        await context.RequireAsync(hold.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        if (hold.HeldBy != context.UserId) throw new SlotUnavailableException("being_booked");
        var now = context.Now;
        if (hold.ExpiresAt <= now) throw new SlotUnavailableException("hold_expired");

        var errors = new List<ValidationError>();
        if (command.PatientId == Guid.Empty) errors.Add(new("patientId", "required"));
        if (!Enum.IsDefined(command.ReasonCategory)) errors.Add(new("reasonCategory", "invalid"));
        if (!Enum.IsDefined(command.Channel)) errors.Add(new("channel", "invalid"));
        var reason = ScheduleService.Clean(command.ReasonText);
        if (reason?.Length > MaxReason) errors.Add(new("reasonText", "too_long"));
        SchedulingValidationException.ThrowIfAny(errors);
        // Only a patient registered at the hold's facility: a membership at one facility never reaches another's patients.
        var patient = (await context.PatientsAsync([command.PatientId], cancellationToken)).GetValueOrDefault(command.PatientId) is { } p && p.FacilityId == hold.FacilityId
            ? p
            : throw new SchedulingValidationException([new("patientId", "unknown")]);
        var (_, zone) = await context.FacilityAsync(hold.FacilityId, cancellationToken);

        Appointment appointment;
        try
        {
            appointment = await transaction.RunAsync([(hold.CalendarId, ScheduleRules.LocalDate(hold.Start, zone))], async () =>
        {
            // The hold proved the slot was free; the template or another booking may have changed since.
            if (await BookableEndAsync(hold.FacilityId, hold.CalendarId, hold.Start, hold.VisitType, zone, cancellationToken) != hold.End)
                throw new SlotUnavailableException("slot_unavailable");
            var kind = await db.Templates.Where(t => t.FacilityId == hold.FacilityId && t.CalendarId == hold.CalendarId).Select(t => t.CalendarKind).SingleAsync(cancellationToken);
            await db.SlotHolds.Where(h => h.Id == hold.Id).ExecuteDeleteAsync(cancellationToken);
            return await CreateAsync(Draft(hold.FacilityId, hold.CalendarId, kind, patient.Id, hold.VisitType, hold.Start, hold.End, command.ReasonCategory, reason, command.Channel, now),
                SchedulingAudit.Book, cancellationToken, key);
        }, cancellationToken);
        }
        // An identical request that raced this one took the slot (the doctor-day lock made us wait for it): answer with its outcome.
        catch (SlotUnavailableException) when (key is not null)
        {
            return await ReplayAsync(key, cancellationToken) ?? throw new SlotUnavailableException("slot_unavailable");
        }
        return await ViewAsync(appointment, patient, now);
    }

    /// <summary>
    /// The appointment this user already booked with this key, as the desk sees it now (nothing about the patient is kept with the key;
    /// the view is built from the appointment). Clears expired keys of the tenant first. Another request under the same key is refused.
    /// </summary>
    async Task<AppointmentView?> ReplayAsync(IdempotencyHashes key, CancellationToken cancellationToken)
    {
        var me = context.UserId;
        var now = context.Now;
        // shortcut: expired keys are cleared by the next keyed booking of the tenant, not by a scheduled job; a quiet tenant keeps
        // some stale hashes (no patient data) until then. Upgrade to a nightly sweep with the retention jobs.
        await db.Appointments.Where(a => a.IdempotencyKeyHash != null && a.BookedAt < now - IdempotencyHashes.Lifetime)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IdempotencyKeyHash, (string?)null).SetProperty(a => a.IdempotencyRequestHash, (string?)null), cancellationToken);
        var first = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.BookedBy == me && a.IdempotencyKeyHash == key.Key, cancellationToken);
        if (first is null) return null;
        if (first.IdempotencyRequestHash != key.Request) throw new IdempotencyKeyException("reused");
        await context.RequireAsync(first.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        var patient = (await context.PatientsAsync([first.PatientId], cancellationToken)).GetValueOrDefault(first.PatientId)
            ?? throw new SchedulingValidationException([new("patientId", "unknown")]);
        return await ViewAsync(first, patient, now);
    }

    public async Task<AppointmentView> WalkInAsync(WalkIn command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        var errors = new List<ValidationError>();
        if (command.PatientId == Guid.Empty) errors.Add(new("patientId", "required"));
        if (!Enum.IsDefined(command.VisitType)) errors.Add(new("visitType", "invalid"));
        if (!Enum.IsDefined(command.ReasonCategory)) errors.Add(new("reasonCategory", "invalid"));
        SchedulingValidationException.ThrowIfAny(errors);
        // As booking: only a patient registered at this facility.
        var patient = (await context.PatientsAsync([command.PatientId], cancellationToken)).GetValueOrDefault(command.PatientId) is { } p && p.FacilityId == command.FacilityId
            ? p
            : throw new SchedulingValidationException([new("patientId", "unknown")]);
        var (_, zone) = await context.FacilityAsync(command.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId!.Value);
        var today = ScheduleRules.LocalDate(now, zone);
        // shortcut: a read without a lock, so two desks checking the same patient in at the same instant both pass; it is a warning
        // for the desk, not an integrity rule (each visit still takes its own slot). Upgrade to the doctor-day lock if that is seen.
        if (!command.AllowRepeat)
        {
            var (dayStart, dayEnd) = ScheduleRules.DayBounds(today, zone);
            if (await db.Appointments.AsNoTracking().AnyAsync(a => a.FacilityId == command.FacilityId && a.PatientId == patient.Id && a.Start >= dayStart && a.Start < dayEnd
                    && a.State != AppointmentState.Done && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow && a.State != AppointmentState.NeedsRebooking,
                    cancellationToken))
                throw new SlotUnavailableException("already_today");
        }

        // Doctors only; a chosen one must be an active doctor of this facility.
        var templates = await db.Templates.AsNoTracking()
            .Where(t => t.FacilityId == command.FacilityId && t.CalendarKind == CalendarKind.Doctor && (command.CalendarId == null || t.CalendarId == command.CalendarId))
            .ToListAsync(cancellationToken);
        var doctors = new List<ScheduleTemplate>();
        foreach (var t in templates) if (await context.IsActiveAsync(command.FacilityId, t.CalendarId, CalendarKind.Doctor, cancellationToken)) doctors.Add(t);
        if (command.CalendarId is not null && doctors.Count == 0) throw new SchedulingValidationException([new("calendarId", "unknown")]);

        foreach (var (calendar, start) in await FreeStartsAsync(command, doctors, today, zone, now, me, cancellationToken))
        {
            try
            {
                // The booking path: the doctor-day lock, then the template and overlap re-check (another desk may have booked it since
                // the read above), SCHEDULE_SLOT rows (the unique index refuses a double booking), status, outbox and audit events.
                var visit = await transaction.RunAsync([(calendar, today)], async () =>
                {
                    var end = await BookableEndAsync(command.FacilityId, calendar, start, command.VisitType, zone, cancellationToken);
                    if (await db.SlotHolds.AnyAsync(h => h.CalendarId == calendar && h.FacilityId == command.FacilityId && h.Start < end && start < h.End
                            && h.ExpiresAt > now && h.HeldBy != me, cancellationToken))
                        throw new SlotUnavailableException("being_booked");
                    return await CreateAsync(Draft(command.FacilityId, calendar, CalendarKind.Doctor, patient.Id, command.VisitType, start, end, command.ReasonCategory, null,
                        BookingChannel.InPerson, now, walkIn: true), SchedulingAudit.WalkIn, cancellationToken);
                }, cancellationToken);
                return await ViewAsync(visit, patient, now);
            }
            catch (SlotUnavailableException e) when (e.Code is "slot_unavailable" or "being_booked" or "not_bookable" or "closed")
            {
                // Taken in the meantime: try the next free slot.
            }
        }

        // No free slot today: queue the walk-in under the chosen doctor without taking any slot (zero length at arrival, no
        // SCHEDULE_SLOT rows), so the calendar is never double-booked. "Next available" queues with a doctor working today (#912);
        // only when nobody works today is the desk asked to choose one.
        if ((command.CalendarId ?? await LeastBusyDoctorTodayAsync(command.FacilityId, doctors, today, zone, cancellationToken)) is not { } doctor)
            throw new SlotUnavailableException("no_free_slot");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var queued = await CreateAsync(Draft(command.FacilityId, doctor, CalendarKind.Doctor, patient.Id, command.VisitType, now, now, command.ReasonCategory, null,
            BookingChannel.InPerson, now, walkIn: true, unscheduled: true), SchedulingAudit.WalkIn, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(queued, patient, now);
    }

    /// <summary>Free slots a walk-in tries, earliest first, before it is queued unscheduled (each may be taken by another desk meanwhile).</summary>
    const int MaxWalkInAttempts = 5;

    /// <summary>
    /// The doctor a "next available" walk-in with no free slot waits for: of the doctors with a session today (template less leave),
    /// the one with the fewest open visits today, then by id so two desks agree. Null when nobody works today.
    /// </summary>
    async Task<Guid?> LeastBusyDoctorTodayAsync(Guid facilityId, List<ScheduleTemplate> doctors, DateOnly today, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var exceptions = await context.ExceptionsAsync(facilityId, today, today, null, cancellationToken);
        var working = doctors.Where(t => ScheduleRules.Day(t, today, zone, exceptions.Where(x => x.CalendarId == t.CalendarId)).Slots.Count > 0)
            .Select(t => t.CalendarId).ToList();
        if (working.Count == 0) return null;
        var (dayStart, dayEnd) = ScheduleRules.DayBounds(today, zone);
        var open = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == facilityId && working.Contains(a.CalendarId) && a.Start >= dayStart && a.Start < dayEnd
                && a.State != AppointmentState.Done && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow && a.State != AppointmentState.NeedsRebooking)
            .GroupBy(a => a.CalendarId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return working.OrderBy(id => open.GetValueOrDefault(id)).ThenBy(id => id).First();
    }

    /// <summary>
    /// Slot starts from now on today that fit the visit type, free of appointments and of other users' holds, earliest first
    /// across the doctors. A read without the lock: the booking path re-checks the one it takes.
    /// </summary>
    async Task<List<(Guid Calendar, DateTimeOffset Start)>> FreeStartsAsync(WalkIn command, List<ScheduleTemplate> doctors, DateOnly today, TimeZoneInfo zone,
        DateTimeOffset now, Guid me, CancellationToken cancellationToken)
    {
        var ids = doctors.Select(d => d.CalendarId).ToList();
        var (from, to) = ScheduleRules.DayBounds(today, zone);
        var exceptions = await context.ExceptionsAsync(command.FacilityId, today, today, null, cancellationToken);
        var taken = await db.Appointments.AsNoTracking()
            .Where(a => a.FacilityId == command.FacilityId && ids.Contains(a.CalendarId) && a.Start < to && a.End > from && !a.Unscheduled
                && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow && a.State != AppointmentState.NeedsRebooking)
            .Select(a => new { a.CalendarId, a.Start, a.End }).ToListAsync(cancellationToken);
        var held = await db.SlotHolds.AsNoTracking()
            .Where(h => h.FacilityId == command.FacilityId && ids.Contains(h.CalendarId) && h.ExpiresAt > now && h.HeldBy != me)
            .Select(h => new { h.CalendarId, h.Start, h.End }).ToListAsync(cancellationToken);
        return [.. doctors
            .SelectMany(t =>
            {
                var day = ScheduleRules.Day(t, today, zone, exceptions.Where(x => x.CalendarId == t.CalendarId));
                var length = ScheduleRules.SlotsFor(t, command.VisitType);
                return day.Slots.Where(s => s.Start >= now).Select(s => (Calendar: t.CalendarId, s.Start, End: ScheduleRules.SpanEnd(day, s.Start, length)));
            })
            .Where(c => c.End is { } end
                && !taken.Any(a => a.CalendarId == c.Calendar && a.Start < end && c.Start < a.End)
                && !held.Any(h => h.CalendarId == c.Calendar && h.Start < end && c.Start < h.End))
            .OrderBy(c => c.Start).ThenBy(c => c.Calendar)
            .Take(MaxWalkInAttempts)
            .Select(c => (c.Calendar, c.Start))];
    }

    static Appointment Draft(Guid facilityId, Guid calendarId, CalendarKind kind, Guid patientId, VisitType type, DateTimeOffset start, DateTimeOffset end,
        ReasonCategory reason, string? reasonText, BookingChannel channel, DateTimeOffset now, bool walkIn = false, bool unscheduled = false, Guid? seriesId = null) => new()
    {
        Id = Guid.CreateVersion7(), FacilityId = facilityId, CalendarId = calendarId, CalendarKind = kind, PatientId = patientId, VisitType = type,
        Start = start, End = end, State = AppointmentState.Booked, ReasonCategory = reason, ReasonText = reasonText, Channel = channel,
        WalkIn = walkIn, Unscheduled = unscheduled, SeriesId = seriesId, BookedAt = now, ChangedAt = now,
    };

    /// <summary>
    /// Writes a new Booked visit inside the caller's transaction: the appointment, its SCHEDULE_SLOT rows (none when unscheduled;
    /// the unique index refuses a double booking of any 5-minute unit), the first status event, AppointmentBooked.v1 and the audit
    /// event of <paramref name="action"/>.
    /// </summary>
    async Task<Appointment> CreateAsync(Appointment draft, string action, CancellationToken cancellationToken, IdempotencyHashes? key = null)
    {
        var booked = new Appointment
        {
            Id = draft.Id, TenantId = context.TenantId, FacilityId = draft.FacilityId, CalendarId = draft.CalendarId, CalendarKind = draft.CalendarKind,
            PatientId = draft.PatientId, VisitType = draft.VisitType, Start = draft.Start, End = draft.End, State = AppointmentState.Booked,
            ReasonCategory = draft.ReasonCategory, ReasonText = draft.ReasonText, Channel = draft.Channel, WalkIn = draft.WalkIn, Unscheduled = draft.Unscheduled, SeriesId = draft.SeriesId,
            BookedBy = context.UserId, BookedAt = draft.BookedAt, ChangedBy = context.UserId, ChangedAt = draft.ChangedAt,
            IdempotencyKeyHash = key?.Key, IdempotencyRequestHash = key?.Request,
        };
        db.Appointments.Add(booked);
        if (!booked.Unscheduled) db.ScheduleSlots.AddRange(ScheduleSlot.For(booked));
        db.StatusEvents.Add(new VisitStatusEvent
        {
            Id = Guid.CreateVersion7(), TenantId = booked.TenantId, AppointmentId = booked.Id, State = AppointmentState.Booked, ActorUserId = context.UserId, OccurredAt = booked.BookedAt,
        });
        db.Outbox.Add(Booked(booked, booked.BookedAt));
        await db.SaveChangesAsync(cancellationToken);
        await context.AuditAsync(action, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, booked.Id, booked.PatientId, cancellationToken);
        return booked;
    }

    /// <summary>After the commit: a new card on the open boards, and the visit as reception sees it.</summary>
    async Task<AppointmentView> ViewAsync(Appointment a, Medvya.Patients.Application.PatientSummary patient, DateTimeOffset now)
    {
        await FrontDeskService.NotifyAsync(notifier, log, new(a.TenantId, a.FacilityId, a.Id, a.State, now));
        return new(a.Id, AppointmentReference.Of(a.Id), a.FacilityId, a.CalendarId, patient.Id, patient.FullName, patient.Mrn,
            a.VisitType, a.Start, a.End, a.State, a.ReasonCategory, a.Channel, a.WalkIn, a.Unscheduled);
    }

    public async Task<AppointmentChip> RescheduleAsync(Guid appointmentId, RescheduleAppointment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var seen = await FindAsync(appointmentId, cancellationToken);
        if (!VisitFlow.CanReschedule(seen.State)) throw new VisitTransitionException("invalid_transition");
        if (command.CalendarId == seen.CalendarId && command.Start == seen.Start) throw new SchedulingValidationException([new("start", "unchanged")]);
        var (_, zone) = await context.FacilityAsync(seen.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId!.Value);
        if (command.Start < now) throw new SlotUnavailableException("in_past");

        // Both doctor-days are locked, in the transaction's fixed order, so a booking of either slot waits for this move.
        var days = new[] { (seen.CalendarId, ScheduleRules.LocalDate(seen.Start, zone)), (command.CalendarId, ScheduleRules.LocalDate(command.Start, zone)) };
        var moved = await transaction.RunAsync(days, async () =>
        {
            var end = await BookableEndAsync(seen.FacilityId, command.CalendarId, command.Start, seen.VisitType, zone, cancellationToken, moving: seen);
            if (await db.SlotHolds.AnyAsync(h => h.CalendarId == command.CalendarId && h.FacilityId == seen.FacilityId && h.Start < end && command.Start < h.End
                    && h.ExpiresAt > now && h.HeldBy != me, cancellationToken))
                throw new SlotUnavailableException("being_booked");
            if (await db.Appointments.Where(a => a.Id == seen.Id && a.State == seen.State && a.CalendarId == seen.CalendarId && a.Start == seen.Start)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AppointmentState.Booked).SetProperty(a => a.CalendarId, command.CalendarId).SetProperty(a => a.Start, command.Start).SetProperty(a => a.End, end).SetProperty(a => a.Unscheduled, false)
                        .SetProperty(a => a.ChangedBy, me).SetProperty(a => a.ChangedAt, now), cancellationToken) == 0)
                throw new VisitTransitionException("changed"); // checked in, cancelled or moved by someone else since the desk looked
            var after = Moved(seen, command.CalendarId, command.Start, end, me, now);
            after.State = AppointmentState.Booked;
            if (seen.State == AppointmentState.NeedsRebooking) // back on the schedule: the board and the history show it
                db.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = seen.TenantId, AppointmentId = seen.Id, State = AppointmentState.Booked, ActorUserId = me, OccurredAt = now });
            await db.ScheduleSlots.Where(s => s.AppointmentId == seen.Id).ExecuteDeleteAsync(cancellationToken); // the old slot is free
            db.ScheduleSlots.AddRange(ScheduleSlot.For(after)); // and the new one taken: the unique index refuses a double booking
            db.Reschedules.Add(new AppointmentReschedule
            {
                Id = Guid.CreateVersion7(), TenantId = seen.TenantId, AppointmentId = seen.Id, FromCalendarId = seen.CalendarId, FromStart = seen.Start, FromEnd = seen.End,
                ToCalendarId = after.CalendarId, ToStart = after.Start, ToEnd = after.End, ActorUserId = me, OccurredAt = now,
            });
            db.Outbox.Add(Event("AppointmentRescheduled.v1", after, now, new
            {
                appointmentId = after.Id, facilityId = after.FacilityId, patientId = after.PatientId, fromCalendarId = seen.CalendarId, fromStart = seen.Start,
                calendarId = after.CalendarId, start = after.Start, end = after.End,
            }));
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.Reschedule, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, seen.Id, seen.PatientId, cancellationToken);
            return after;
        }, cancellationToken);
        await FrontDeskService.NotifyAsync(notifier, log, new(moved.TenantId, moved.FacilityId, moved.Id, moved.State, now)); // the card moves or leaves today's board
        return await ChipAsync(moved, cancellationToken);
    }

    public async Task<AppointmentChip> CancelAsync(Guid appointmentId, CancelAppointment command, CancellationToken cancellationToken = default)
    {
        var note = CleanCancel(command);
        var seen = await FindAsync(appointmentId, cancellationToken);
        if (!VisitFlow.CanCancel(seen.State)) throw new VisitTransitionException("invalid_transition");
        return await EndAsync(seen, AppointmentState.Cancelled, SchedulingAudit.Cancel, "AppointmentCancelled.v1", command.Reason, note, cancellationToken);
    }

    /// <summary>Validates the reason and note of a cancellation and returns the cleaned note.</summary>
    static string? CleanCancel(CancelAppointment command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var note = ScheduleService.Clean(command.Note);
        var errors = new List<ValidationError>();
        if (command.Reason is not { } reason || !Enum.IsDefined(reason)) errors.Add(new("reason", "required"));
        else if (reason == CancelReason.Other && note is null) errors.Add(new("note", "required"));
        if (note?.Length > MaxReason) errors.Add(new("note", "too_long"));
        SchedulingValidationException.ThrowIfAny(errors);
        return note;
    }

    public async Task<IReadOnlyList<AppointmentChip>> CancelFollowingAsync(Guid appointmentId, CancelAppointment command, CancellationToken cancellationToken = default)
    {
        var note = CleanCancel(command);
        var seen = await FindAsync(appointmentId, cancellationToken);
        if (seen.SeriesId is not { } series) throw new SchedulingValidationException([new("seriesId", "not_in_series")]);
        if (!VisitFlow.CanCancel(seen.State)) throw new VisitTransitionException("invalid_transition");
        var following = await db.Appointments.AsNoTracking()
            .Where(a => a.SeriesId == series && a.Start > seen.Start && (a.State == AppointmentState.Booked || a.State == AppointmentState.CheckedIn))
            .OrderBy(a => a.Start).ToListAsync(cancellationToken);

        // Each visit ends in its own doctor-day transaction. The chosen one tells the patient; the rest are quiet.
        var chips = new List<AppointmentChip> { await EndAsync(seen, AppointmentState.Cancelled, SchedulingAudit.Cancel, "AppointmentCancelled.v1", command.Reason, note, cancellationToken) };
        foreach (var next in following)
        {
            try
            {
                chips.Add(await EndAsync(next, AppointmentState.Cancelled, SchedulingAudit.Cancel, "AppointmentCancelled.v1", command.Reason, note, cancellationToken, quiet: true));
            }
            catch (VisitTransitionException)
            {
                // Checked in, cancelled or moved by someone else meanwhile: it stays as it is.
            }
        }
        return chips;
    }

    public async Task<AppointmentChip> MarkNoShowAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var seen = await FindAsync(appointmentId, cancellationToken);
        if (!VisitFlow.CanMarkNoShow(seen.State)) throw new VisitTransitionException("invalid_transition");
        if (context.Now < seen.Start) throw new VisitTransitionException("not_started"); // nobody is a no-show before the visit starts
        return await EndAsync(seen, AppointmentState.NoShow, SchedulingAudit.NoShow, "AppointmentNoShow.v1", null, null, cancellationToken);
    }

    /// <summary>
    /// Cancel or no-show: the state changes only from the state the desk saw (a concurrent check-in or move wins and this one is
    /// "changed"), the visit's slots are freed for booking again, and the status event (with the reason) and the outbox event are
    /// written with the audit event, all under the doctor-day lock of the visit.
    /// </summary>
    async Task<AppointmentChip> EndAsync(Appointment seen, AppointmentState to, string action, string eventType, CancelReason? reason, string? note, CancellationToken cancellationToken, bool quiet = false)
    {
        var (_, zone) = await context.FacilityAsync(seen.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId);
        var ended = await transaction.RunAsync([(seen.CalendarId, ScheduleRules.LocalDate(seen.Start, zone))], async () =>
        {
            if (await db.Appointments.Where(a => a.Id == seen.Id && a.State == seen.State && a.CalendarId == seen.CalendarId && a.Start == seen.Start)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, to).SetProperty(a => a.ChangedBy, me).SetProperty(a => a.ChangedAt, now), cancellationToken) == 0)
                throw new VisitTransitionException("changed");
            await db.ScheduleSlots.Where(s => s.AppointmentId == seen.Id).ExecuteDeleteAsync(cancellationToken); // the slot can be booked again
            db.StatusEvents.Add(new VisitStatusEvent
            {
                Id = Guid.CreateVersion7(), TenantId = seen.TenantId, AppointmentId = seen.Id, State = to, ActorUserId = me, OccurredAt = now, CancelReason = reason, Note = note,
            });
            db.Outbox.Add(Event(eventType, seen, now, new { appointmentId = seen.Id, facilityId = seen.FacilityId, calendarId = seen.CalendarId, patientId = seen.PatientId, start = seen.Start, reason, quiet }));
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(action, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, seen.Id, seen.PatientId, cancellationToken);
            var after = Moved(seen, seen.CalendarId, seen.Start, seen.End, me, now);
            after.State = to;
            return after;
        }, cancellationToken);
        await FrontDeskService.NotifyAsync(notifier, log, new(ended.TenantId, ended.FacilityId, ended.Id, to, now)); // the card leaves the board
        return await ChipAsync(ended, cancellationToken);
    }

    /// <summary>The visit as the desk saw it, after checking the caller may book at its facility.</summary>
    public async Task<IReadOnlyList<RescheduleView>> HistoryAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var visit = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        await context.RequireAsync(visit.FacilityId, SchedulingAccess.CanView, cancellationToken);
        var moves = await db.Reschedules.AsNoTracking().Where(r => r.AppointmentId == appointmentId).OrderBy(r => r.OccurredAt).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        if (moves.Count == 0) return [];
        // Doctors and staff by display name (an offboarded doctor keeps theirs), rooms by resource name.
        var ids = moves.SelectMany(m => new[] { m.FromCalendarId, m.ToCalendarId, m.ActorUserId ?? Guid.Empty }).Distinct().ToList();
        var names = new Dictionary<Guid, string>(await context.DisplayNamesAsync(ids, cancellationToken));
        foreach (var r in await db.Resources.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken))
            names[r.Id] = r.Name;
        string Name(Guid? id) => id is { } g ? names.GetValueOrDefault(g) ?? "" : "";
        return [.. moves.Select(m => new RescheduleView(Name(m.FromCalendarId), m.FromStart, m.FromEnd, Name(m.ToCalendarId), m.ToStart, m.ToEnd,
            Name(m.ActorUserId), m.OccurredAt, m.ReassignReason))];
    }

    async Task<Appointment> FindAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var visit = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException();
        await context.RequireAsync(visit.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        return visit;
    }

    async Task<AppointmentChip> ChipAsync(Appointment a, CancellationToken cancellationToken) =>
        SchedulingContext.Chip(a, await context.PatientsAsync([a.PatientId], cancellationToken));

    /// <summary>A copy of the visit in its new slot; the original stays as it was, so a retried attempt starts from the same picture.</summary>
    static Appointment Moved(Appointment a, Guid calendarId, DateTimeOffset start, DateTimeOffset end, Guid? by, DateTimeOffset at) => new()
    {
        Id = a.Id, TenantId = a.TenantId, FacilityId = a.FacilityId, CalendarId = calendarId, CalendarKind = a.CalendarKind, PatientId = a.PatientId, VisitType = a.VisitType, SeriesId = a.SeriesId,
        Start = start, End = end, State = a.State, ReasonCategory = a.ReasonCategory, Channel = a.Channel, BookedBy = a.BookedBy, BookedAt = a.BookedAt, ChangedBy = by, ChangedAt = at,
    };

    public async Task<IReadOnlyList<SeriesOccurrence>> PreviewSeriesAsync(SeriesPattern pattern, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        await context.RequireAsync(pattern.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        var errors = new List<ValidationError>();
        if (pattern.Count is < 2 or > SeriesPattern.MaxCount) errors.Add(new("count", "out_of_range"));
        if (!pattern.Monthly && pattern.IntervalWeeks is < 1 or > SeriesPattern.MaxIntervalWeeks) errors.Add(new("intervalWeeks", "out_of_range"));
        if (!Enum.IsDefined(pattern.VisitType)) errors.Add(new("visitType", "invalid"));
        SchedulingValidationException.ThrowIfAny(errors);
        var (_, zone) = await context.FacilityAsync(pattern.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId!.Value);

        var result = new List<SeriesOccurrence>();
        foreach (var (start, i) in ScheduleRules.SeriesStarts(pattern.FirstStart, pattern.IntervalWeeks, pattern.Count, zone, pattern.Monthly).Select((s, i) => (s, i)))
        {
            // A read without the lock; booking re-checks under it. Same checks as a hold.
            string? conflict = null;
            try
            {
                if (start < now) throw new SlotUnavailableException("in_past");
                var end = await BookableEndAsync(pattern.FacilityId, pattern.CalendarId, start, pattern.VisitType, zone, cancellationToken);
                if (await db.SlotHolds.AnyAsync(h => h.CalendarId == pattern.CalendarId && h.FacilityId == pattern.FacilityId && h.Start < end && start < h.End
                        && h.ExpiresAt > now && h.HeldBy != me, cancellationToken))
                    conflict = "being_booked";
            }
            catch (SlotUnavailableException e)
            {
                conflict = e.Code;
            }
            result.Add(new(i, start, conflict));
        }
        return result;
    }

    public async Task<SeriesResult> BookSeriesAsync(BookSeries command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        var errors = new List<ValidationError>();
        if (command.PatientId == Guid.Empty) errors.Add(new("patientId", "required"));
        if (!Enum.IsDefined(command.VisitType)) errors.Add(new("visitType", "invalid"));
        if (!Enum.IsDefined(command.ReasonCategory)) errors.Add(new("reasonCategory", "invalid"));
        if (!Enum.IsDefined(command.Channel)) errors.Add(new("channel", "invalid"));
        var reason = ScheduleService.Clean(command.ReasonText);
        if (reason?.Length > MaxReason) errors.Add(new("reasonText", "too_long"));
        if (command.Starts.Count is < 1 or > SeriesPattern.MaxCount || command.Starts.Distinct().Count() != command.Starts.Count) errors.Add(new("starts", "out_of_range"));
        SchedulingValidationException.ThrowIfAny(errors);
        var patient = (await context.PatientsAsync([command.PatientId], cancellationToken)).GetValueOrDefault(command.PatientId) is { } p && p.FacilityId == command.FacilityId
            ? p
            : throw new SchedulingValidationException([new("patientId", "unknown")]);
        var (_, zone) = await context.FacilityAsync(command.FacilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId!.Value);

        var seriesId = Guid.CreateVersion7();
        var booked = new List<SeriesBooked>();
        foreach (var start in command.Starts.Order())
        {
            try
            {
                if (start < now) throw new SlotUnavailableException("in_past");
                // shortcut: each occurrence commits on its own, so a crash mid-series leaves the earlier ones booked; the desk re-opens
                // the series and books the rest. Upgrade to one transaction if partial series prove a problem.
                var visit = await transaction.RunAsync([(command.CalendarId, ScheduleRules.LocalDate(start, zone))], async () =>
                {
                    var end = await BookableEndAsync(command.FacilityId, command.CalendarId, start, command.VisitType, zone, cancellationToken);
                    if (await db.SlotHolds.AnyAsync(h => h.CalendarId == command.CalendarId && h.FacilityId == command.FacilityId && h.Start < end && start < h.End
                            && h.ExpiresAt > now && h.HeldBy != me, cancellationToken))
                        throw new SlotUnavailableException("being_booked");
                    var kind = await db.Templates.Where(t => t.FacilityId == command.FacilityId && t.CalendarId == command.CalendarId).Select(t => t.CalendarKind).SingleAsync(cancellationToken);
                    return await CreateAsync(Draft(command.FacilityId, command.CalendarId, kind, patient.Id, command.VisitType, start, end, command.ReasonCategory, reason, command.Channel, now,
                        seriesId: seriesId), SchedulingAudit.Book, cancellationToken);
                }, cancellationToken);
                booked.Add(new(start, await ViewAsync(visit, patient, now), null));
            }
            catch (SlotUnavailableException e)
            {
                booked.Add(new(start, null, e.Code));
            }
        }
        return new(seriesId, booked);
    }

    public async Task<PatientView?> FindPatientAsync(Guid facilityId, Guid patientId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanBook, cancellationToken);
        // Patients of this facility only, as front-desk search (MDV-P1-018): another facility's patient is "not found".
        return (await context.PatientsAsync([patientId], cancellationToken)).TryGetValue(patientId, out var p) && p.FacilityId == facilityId ? new(p.Id, p.FullName, p.Mrn) : null;
    }

    /// <summary>
    /// End of a visit of this type starting at <paramref name="start"/>, after checking the calendar is active, the start is a
    /// slot of the template with enough back-to-back slots, and no appointment occupies any of them. When <paramref name="moving"/>
    /// is a visit being rescheduled, its own time does not count as taken and the calendar must be of its kind.
    /// </summary>
    async Task<DateTimeOffset> BookableEndAsync(Guid facilityId, Guid calendarId, DateTimeOffset start, VisitType type, TimeZoneInfo zone, CancellationToken cancellationToken,
        Appointment? moving = null)
    {
        var template = await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.FacilityId == facilityId && t.CalendarId == calendarId, cancellationToken);
        if (template is null || (moving is not null && template.CalendarKind != moving.CalendarKind)
            || !await context.IsActiveAsync(facilityId, calendarId, template.CalendarKind, cancellationToken))
            throw new SlotUnavailableException("not_bookable"); // e.g. the doctor is not working at this facility, or a room for a doctor's visit
        var date = ScheduleRules.LocalDate(start, zone);
        if ((await context.ClosuresAsync(facilityId, date, date, cancellationToken)).Count > 0) throw new SlotUnavailableException("closed"); // a holiday at the location (MDV-P1-005)
        var exceptions = await context.ExceptionsAsync(facilityId, date, date, calendarId, cancellationToken);
        var end = ScheduleRules.SpanEnd(ScheduleRules.Day(template, date, zone, exceptions), start, ScheduleRules.SlotsFor(template, type))
            ?? throw new SlotUnavailableException("not_bookable"); // outside hours, in a break or too long for what is left
        var earliest = start.AddMinutes(-ScheduleRules.MaxVisitMinutes); // no visit is longer, so the index seek has a lower bound
        var self = moving?.Id;
        if (await db.Appointments.AnyAsync(a => a.FacilityId == facilityId && a.CalendarId == calendarId && a.Start < end && start < a.End && a.Start > earliest
                && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow && a.State != AppointmentState.NeedsRebooking && a.Id != self && !a.Unscheduled, cancellationToken))
            throw new SlotUnavailableException("slot_unavailable");
        return end;
    }

    /// <summary>AppointmentBooked.v1: opaque ids, instants and flags only.</summary>
    static OutboxEvent Booked(Appointment a, DateTimeOffset now) =>
        Event("AppointmentBooked.v1", a, now, new
        {
            appointmentId = a.Id, facilityId = a.FacilityId, calendarId = a.CalendarId, patientId = a.PatientId, start = a.Start, end = a.End, walkIn = a.WalkIn, unscheduled = a.Unscheduled,
        });

    /// <summary>An outbox event of the visit. Payloads are opaque ids, instants and list values, never names or notes.</summary>
    static OutboxEvent Event(string type, Appointment a, DateTimeOffset now, object payload) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = a.TenantId, Type = type, AggregateId = a.Id, OccurredAt = now, Payload = JsonSerializer.Serialize(payload),
    };
}
