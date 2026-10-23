using System.Text.Json;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Slot holds and booking (MDV-P1-021). Every write runs in a <see cref="DoctorDayTransaction"/> (MDV-P1-022) and re-checks that the slots are still slots of the
/// template, free of appointments and not held by someone else. The booking, its first status event and AppointmentBooked.v1
/// are written in one transaction, with the attempt's audit event (MDV-P0-017), which now carries the patient id.
/// </summary>
public sealed class BookingService(SchedulingDbContext db, SchedulingContext context, DoctorDayTransaction transaction, IOptions<SchedulingOptions> options) : IBookingService
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

    public async Task<AppointmentView> BookAsync(BookAppointment command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var hold = await db.SlotHolds.AsNoTracking().SingleOrDefaultAsync(h => h.Id == command.HoldId, cancellationToken)
            ?? throw new SlotUnavailableException("hold_expired"); // released, replaced or cleaned up after expiry
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

        var appointment = await transaction.RunAsync([(hold.CalendarId, ScheduleRules.LocalDate(hold.Start, zone))], async () =>
        {
            // The hold proved the slot was free; the template or another booking may have changed since.
            if (await BookableEndAsync(hold.FacilityId, hold.CalendarId, hold.Start, hold.VisitType, zone, cancellationToken) != hold.End)
                throw new SlotUnavailableException("slot_unavailable");
            var kind = await db.Templates.Where(t => t.FacilityId == hold.FacilityId && t.CalendarId == hold.CalendarId).Select(t => t.CalendarKind).SingleAsync(cancellationToken);
            var booked = new Appointment
            {
                Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = hold.FacilityId, CalendarId = hold.CalendarId, CalendarKind = kind,
                PatientId = patient.Id, VisitType = hold.VisitType, Start = hold.Start, End = hold.End, State = AppointmentState.Booked,
                ReasonCategory = command.ReasonCategory, ReasonText = reason, Channel = command.Channel,
                BookedBy = context.UserId, BookedAt = now, ChangedBy = context.UserId, ChangedAt = now,
            };
            db.Appointments.Add(booked);
            db.ScheduleSlots.AddRange(ScheduleSlot.For(booked)); // the database refuses a second booking of any of these 5-minute units
            db.StatusEvents.Add(new VisitStatusEvent
            {
                Id = Guid.CreateVersion7(), TenantId = booked.TenantId, AppointmentId = booked.Id, State = AppointmentState.Booked, ActorUserId = context.UserId, OccurredAt = now,
            });
            db.Outbox.Add(Booked(booked, now));
            await db.SlotHolds.Where(h => h.Id == hold.Id).ExecuteDeleteAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.Book, SchedulingAudit.AppointmentResource, SchedulingAudit.Treatment, booked.Id, booked.PatientId, cancellationToken);
            return booked;
        }, cancellationToken);
        return new(appointment.Id, AppointmentReference.Of(appointment.Id), appointment.FacilityId, appointment.CalendarId, patient.Id, patient.FullName, patient.Mrn,
            appointment.VisitType, appointment.Start, appointment.End, appointment.State, appointment.ReasonCategory, appointment.Channel);
    }

    public async Task<PatientView?> FindPatientAsync(Guid facilityId, Guid patientId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanBook, cancellationToken);
        // Patients of this facility only, as front-desk search (MDV-P1-018): another facility's patient is "not found".
        return (await context.PatientsAsync([patientId], cancellationToken)).TryGetValue(patientId, out var p) && p.FacilityId == facilityId ? new(p.Id, p.FullName, p.Mrn) : null;
    }

    /// <summary>
    /// End of a visit of this type starting at <paramref name="start"/>, after checking the calendar is active, the start is a
    /// slot of the template with enough back-to-back slots, and no appointment occupies any of them.
    /// </summary>
    async Task<DateTimeOffset> BookableEndAsync(Guid facilityId, Guid calendarId, DateTimeOffset start, VisitType type, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var template = await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.FacilityId == facilityId && t.CalendarId == calendarId, cancellationToken);
        if (template is null || !await context.IsActiveAsync(facilityId, calendarId, template.CalendarKind, cancellationToken))
            throw new SlotUnavailableException("not_bookable"); // e.g. the doctor is not working at this facility
        var end = ScheduleRules.SpanEnd(ScheduleRules.Day(template, ScheduleRules.LocalDate(start, zone), zone), start, ScheduleRules.SlotsFor(template, type))
            ?? throw new SlotUnavailableException("not_bookable"); // outside hours, in a break or too long for what is left
        var earliest = start.AddMinutes(-ScheduleRules.MaxVisitMinutes); // no visit is longer, so the index seek has a lower bound
        if (await db.Appointments.AnyAsync(a => a.FacilityId == facilityId && a.CalendarId == calendarId && a.Start < end && start < a.End && a.Start > earliest
                && a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow, cancellationToken))
            throw new SlotUnavailableException("slot_unavailable");
        return end;
    }

    /// <summary>AppointmentBooked.v1: opaque ids and instants only.</summary>
    static OutboxEvent Booked(Appointment a, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = a.TenantId, Type = "AppointmentBooked.v1", AggregateId = a.Id, OccurredAt = now,
        Payload = JsonSerializer.Serialize(new { appointmentId = a.Id, facilityId = a.FacilityId, calendarId = a.CalendarId, patientId = a.PatientId, start = a.Start, end = a.End }),
    };
}
