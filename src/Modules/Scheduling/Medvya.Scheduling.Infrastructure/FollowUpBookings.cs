using System.Data.Common;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// A consultation's follow-up as a waitlist entry (MDV-P2-043), so the desk books it with the waitlist's offer and accept (same doctor, same reason,
/// slots from the due day). Writes are conditional updates on the consultation's connection and transaction, as for <see cref="ConsultVisits"/>.
/// shortcut: a patient already on the waitlist for another reason gets no second entry ("already_waiting"); merge the two when reception asks.
/// </summary>
public sealed class FollowUpBookings(SchedulingDbContext db, SchedulingContext context, ICurrentTenant tenant) : IFollowUpBookings
{
    public Task<FollowUpBooking?> FindAsync(Guid encounterId, CancellationToken cancellationToken = default) =>
        db.Waitlist.AsNoTracking().Where(w => w.EncounterId == encounterId && w.State != WaitlistState.Removed)
            .Select(w => new FollowUpBooking(w.FollowUp!, w.DueOn!.Value, w.State)).SingleOrDefaultAsync(cancellationToken);

    public async Task<FollowUpBooking?> SetAsync(Guid appointmentId, Guid encounterId, string? period, DateOnly? dueOn, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var shared = new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        var now = context.Now;
        var entry = await shared.Waitlist.SingleOrDefaultAsync(w => w.EncounterId == encounterId, cancellationToken);
        if (entry?.State == WaitlistState.Booked) throw new VisitTransitionException("follow_up_booked");

        if (period is null || dueOn is null)
        {
            if (entry is null || entry.State == WaitlistState.Removed) return null;
            (entry.State, entry.ChangedAt) = (WaitlistState.Removed, now);
        }
        else
        {
            if (entry is null)
            {
                var visit = await shared.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken) ?? throw new AppointmentNotFoundException();
                if (await shared.Waitlist.AnyAsync(w => w.FacilityId == visit.FacilityId && w.PatientId == visit.PatientId && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered), cancellationToken))
                    throw new VisitTransitionException("already_waiting");
                shared.Waitlist.Add(entry = new WaitlistEntry
                {
                    Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = visit.FacilityId, PatientId = visit.PatientId, CalendarId = visit.CalendarId,
                    VisitType = VisitType.FollowUp, ReasonCategory = visit.ReasonCategory, Days = WaitlistEntry.AnyDay, RequestedAt = now, RequestedBy = context.UserId,
                    EncounterId = encounterId,
                });
            }
            // A new choice starts over: any slot held for the old one goes back.
            (entry.FollowUp, entry.DueOn, entry.State, entry.OfferCalendarId, entry.OfferStart, entry.OfferExpiresAt, entry.ChangedAt) = (period, dueOn, WaitlistState.Waiting, null, null, null, now);
        }
        await shared.SlotHolds.Where(h => h.HeldBy == entry.Id).ExecuteDeleteAsync(cancellationToken);
        try
        {
            await shared.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new VisitTransitionException("changed"); // set from another tab in the same moment
        }
        return entry.State == WaitlistState.Removed ? null : new FollowUpBooking(entry.FollowUp!, entry.DueOn!.Value, entry.State);
    }

    /// <summary>
    /// The doctor's follow-ups still to book (MDV-P1-102) of these patients at the facility, the earliest due per patient: date and doctor only.
    /// No access check: the caller has checked the desk's access to the facility.
    /// </summary>
    internal static async Task<Dictionary<Guid, FollowUpDue>> DueAsync(SchedulingDbContext db, Guid facilityId, IReadOnlyCollection<Guid> patientIds,
        IReadOnlyDictionary<Guid, string> calendars, CancellationToken cancellationToken)
    {
        if (patientIds.Count == 0) return [];
        var open = await db.Waitlist.AsNoTracking()
            .Where(w => w.FacilityId == facilityId && patientIds.Contains(w.PatientId) && w.EncounterId != null && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered))
            .OrderBy(w => w.DueOn).ThenBy(w => w.Id).ToListAsync(cancellationToken);
        return open.GroupBy(w => w.PatientId).ToDictionary(g => g.Key, g =>
        {
            var w = g.First();
            return new FollowUpDue(w.Id, w.DueOn!.Value, w.CalendarId, w.CalendarId is { } c ? calendars.GetValueOrDefault(c) : null);
        });
    }

    /// <summary>
    /// A visit booked for the patient on or after a follow-up's due day books that follow-up (MDV-P1-102): its entry is Booked with the visit, and any slot
    /// offered for it goes back. Runs on the booking's transaction.
    /// shortcut: a visit before the due day leaves the line for the desk to dismiss ("booked elsewhere"); match on doctor or visit type if reception asks.
    /// </summary>
    internal static async Task BookDueAsync(SchedulingDbContext db, SchedulingContext context, Appointment booked, CancellationToken cancellationToken)
    {
        var open = await db.Waitlist.AsNoTracking()
            .Where(w => w.FacilityId == booked.FacilityId && w.PatientId == booked.PatientId && w.EncounterId != null && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered))
            .Select(w => new { w.Id, w.DueOn }).ToListAsync(cancellationToken);
        if (open.Count == 0) return;
        var (_, zone) = await context.FacilityAsync(booked.FacilityId, cancellationToken);
        var day = ScheduleRules.LocalDate(booked.Start, zone);
        var ids = open.Where(w => w.DueOn <= day).Select(w => w.Id).ToList();
        if (ids.Count == 0) return;
        await db.Waitlist.Where(w => ids.Contains(w.Id) && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered))
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.State, WaitlistState.Booked).SetProperty(w => w.AppointmentId, booked.Id).SetProperty(w => w.ChangedAt, context.Now), cancellationToken);
        await db.SlotHolds.Where(h => ids.Contains(h.HeldBy)).ExecuteDeleteAsync(cancellationToken);
    }
}
