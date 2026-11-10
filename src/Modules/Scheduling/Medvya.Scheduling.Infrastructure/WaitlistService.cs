using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// The waitlist (MDV-P1-027). An offer is a slot hold keyed by the entry id (so nobody else can hold, book or move into the slot
/// while it is open) written on the booking path under the doctor-day lock, with its outbox event "WaitlistOffered.v1" (ids and
/// the slot start; the patient message names the clinic, date and time only). Accepting books on that same path. Each change is
/// a conditional update from the state the desk saw, so two desks cannot offer or accept the same entry twice.
/// shortcut: accepting books a new visit; moving an existing later visit to the earlier slot is the desk's reschedule, then remove. A patient
/// merge leaves entries on the retired record; re-add them. Upgrade both when reception asks.
/// </summary>
public sealed class WaitlistService(
    SchedulingDbContext db, SchedulingContext context, BookingService booking, DoctorDayTransaction transaction, IOptions<SchedulingOptions> options) : IWaitlistService
{
    /// <summary>How far ahead the desk looks for a slot that suits a waiting patient.</summary>
    const int HorizonDays = 14;

    public async Task<WaitlistList> ListAsync(Guid facilityId, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanBook, cancellationToken);
        var (_, zone) = await context.FacilityAsync(facilityId, cancellationToken);
        var (now, me) = (context.Now, context.UserId!.Value);
        var entries = await db.Waitlist.AsNoTracking().Where(w => w.FacilityId == facilityId && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered))
            .OrderBy(w => w.RequestedAt).ThenBy(w => w.Id).ToListAsync(cancellationToken);
        var patients = await context.PatientsAsync(entries.Select(w => w.PatientId), cancellationToken);
        var calendars = (await context.CalendarsAsync(facilityId, cancellationToken)).ToDictionary(c => c.Id, c => c);
        var matches = await MatchesAsync(facilityId, entries.Where(w => w.Open(now)).ToList(), calendars, zone, now, me, cancellationToken);

        string Name(Guid id) => calendars.TryGetValue(id, out var c) ? c.Name : "";
        return new(facilityId, zone.Id, now, [.. entries.Select(w =>
        {
            patients.TryGetValue(w.PatientId, out var p);
            var offered = w.State == WaitlistState.Offered && w.OfferExpiresAt > now && w.OfferCalendarId is { } oc && w.OfferStart is { } os ? new SlotMatch(oc, Name(oc), os) : null;
            return Of(w, p?.FullName ?? "", p?.Mrn ?? "", w.CalendarId is { } c ? Name(c) : null, offered, matches.GetValueOrDefault(w.Id), now);
        })]);
    }

    /// <summary>
    /// The earliest free slot that suits each open entry, over the next <see cref="HorizonDays"/> days. The free slots of a day are read
    /// once per visit type and shared by every entry, so a long list costs a few queries a day, not a few per entry.
    /// </summary>
    async Task<Dictionary<Guid, SlotMatch>> MatchesAsync(Guid facilityId, List<WaitlistEntry> open, Dictionary<Guid, Calendar> calendars, TimeZoneInfo zone,
        DateTimeOffset now, Guid me, CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, SlotMatch>();
        if (open.Count == 0) return found;
        var doctorIds = calendars.Values.Where(c => c.Kind == CalendarKind.Doctor).Select(c => c.Id).ToList();
        var doctors = await db.Templates.AsNoTracking().Where(t => t.FacilityId == facilityId && t.CalendarKind == CalendarKind.Doctor && doctorIds.Contains(t.CalendarId)).ToListAsync(cancellationToken);
        var today = ScheduleRules.LocalDate(now, zone);
        var closures = await context.ClosuresAsync(facilityId, today, today.AddDays(HorizonDays - 1), cancellationToken);
        var waiting = open.ToList();
        for (var d = 0; d < HorizonDays && waiting.Count > 0; d++)
        {
            var date = today.AddDays(d);
            if (closures.ContainsKey(date)) continue;
            foreach (var type in waiting.Select(w => w.VisitType).Distinct().ToList())
            {
                var free = await booking.FreeStartsAsync(facilityId, type, doctors, date, zone, now, me, int.MaxValue, cancellationToken);
                foreach (var w in waiting.Where(w => w.VisitType == type).ToList())
                {
                    var slot = free.FirstOrDefault(f => w.Suits(f.Calendar, f.Start, zone));
                    if (slot.Calendar == Guid.Empty) continue;
                    found[w.Id] = new SlotMatch(slot.Calendar, calendars[slot.Calendar].Name, slot.Start);
                    waiting.Remove(w);
                }
            }
        }
        return found;
    }

    public async Task<WaitlistView> AddAsync(AddToWaitlist command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        var errors = new List<ValidationError>();
        if (command.PatientId == Guid.Empty) errors.Add(new("patientId", "required"));
        if (!Enum.IsDefined(command.VisitType)) errors.Add(new("visitType", "invalid"));
        if (!Enum.IsDefined(command.ReasonCategory)) errors.Add(new("reasonCategory", "invalid"));
        if (command.Days.Any(d => !Enum.IsDefined(d))) errors.Add(new("days", "invalid"));
        if (command.From is { } from && command.Until is { } until && until < from) errors.Add(new("until", "before_from"));
        SchedulingValidationException.ThrowIfAny(errors);
        // Any patient of the organisation, and only an active doctor of this facility.
        var patient = (await context.PatientsAsync([command.PatientId], cancellationToken)).GetValueOrDefault(command.PatientId) is { } p
            ? p
            : throw new SchedulingValidationException([new("patientId", "unknown")]);
        string? doctor = null;
        if (command.CalendarId is { } cal)
            doctor = (await context.CalendarsAsync(command.FacilityId, cancellationToken)).FirstOrDefault(c => c.Id == cal && c.Kind == CalendarKind.Doctor)?.Name
                ?? throw new SchedulingValidationException([new("calendarId", "unknown")]);
        if (await db.Waitlist.AnyAsync(w => w.FacilityId == command.FacilityId && w.PatientId == patient.Id && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered), cancellationToken))
            throw new SchedulingValidationException([new("patientId", "already_waiting")]);

        var now = context.Now;
        var entry = new WaitlistEntry
        {
            Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, PatientId = patient.Id, CalendarId = command.CalendarId,
            VisitType = command.VisitType, ReasonCategory = command.ReasonCategory, Days = command.Days.Aggregate(0, (m, d) => m | WaitlistEntry.DayBit(d)),
            From = command.From, Until = command.Until, State = WaitlistState.Waiting, RequestedAt = now, RequestedBy = context.UserId, ChangedAt = now,
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Waitlist.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.WaitlistAdd, SchedulingAudit.WaitlistResource, SchedulingAudit.Treatment, entry.Id, entry.PatientId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Of(entry, patient.FullName, patient.Mrn, doctor, null, null, now);
    }

    public async Task<WaitlistView> OfferAsync(Guid id, OfferSlot command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entry = await FindAsync(id, cancellationToken);
        var now = context.Now;
        if (!entry.Open(now)) throw new VisitTransitionException("invalid_transition");
        var (_, zone) = await context.FacilityAsync(entry.FacilityId, cancellationToken);
        if (command.Start < now) throw new SlotUnavailableException("in_past");
        if (!entry.Suits(command.CalendarId, command.Start, zone)
            || !await db.Templates.AnyAsync(t => t.FacilityId == entry.FacilityId && t.CalendarId == command.CalendarId && t.CalendarKind == CalendarKind.Doctor, cancellationToken))
            throw new SlotUnavailableException("not_a_match");

        await transaction.RunAsync([(command.CalendarId, ScheduleRules.LocalDate(command.Start, zone))], async () =>
        {
            var end = await booking.BookableEndAsync(entry.FacilityId, command.CalendarId, command.Start, entry.VisitType, zone, cancellationToken);
            // Another desk's hold or offer on the slot (this entry's own earlier offer is replaced below).
            if (await db.SlotHolds.AnyAsync(h => h.CalendarId == command.CalendarId && h.FacilityId == entry.FacilityId && h.Start < end && command.Start < h.End
                    && h.ExpiresAt > now && h.HeldBy != entry.Id, cancellationToken))
                throw new SlotUnavailableException("being_booked");
            var expires = now + options.Value.OfferHold;
            if (await db.Waitlist.Where(w => w.Id == id && (w.State == WaitlistState.Waiting || (w.State == WaitlistState.Offered && w.OfferExpiresAt <= now)))
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.State, WaitlistState.Offered).SetProperty(w => w.OfferCalendarId, command.CalendarId)
                        .SetProperty(w => w.OfferStart, command.Start).SetProperty(w => w.OfferExpiresAt, expires).SetProperty(w => w.ChangedAt, now), cancellationToken) == 0)
                throw new VisitTransitionException("changed");
            await db.SlotHolds.Where(h => h.HeldBy == id).ExecuteDeleteAsync(cancellationToken);
            db.SlotHolds.Add(new SlotHold
            {
                Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = entry.FacilityId, CalendarId = command.CalendarId, VisitType = entry.VisitType,
                Start = command.Start, End = end, HeldBy = id, ExpiresAt = expires,
            });
            db.Outbox.Add(new OutboxEvent
            {
                Id = Guid.CreateVersion7(), TenantId = context.TenantId, Type = "WaitlistOffered.v1", AggregateId = id, OccurredAt = now,
                Payload = JsonSerializer.Serialize(new { waitlistId = id, facilityId = entry.FacilityId, calendarId = command.CalendarId, patientId = entry.PatientId, start = command.Start }),
            });
            await db.SaveChangesAsync(cancellationToken);
            await context.AuditAsync(SchedulingAudit.WaitlistOffer, SchedulingAudit.WaitlistResource, SchedulingAudit.Treatment, id, entry.PatientId, cancellationToken);
            return 0;
        }, cancellationToken);
        return await ViewAsync(id, cancellationToken);
    }

    public async Task<WaitlistView> DeclineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await FindAsync(id, cancellationToken);
        if (entry.State != WaitlistState.Offered) throw new VisitTransitionException("invalid_transition");
        var now = context.Now;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Waitlist.Where(w => w.Id == id && w.State == WaitlistState.Offered)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.State, WaitlistState.Waiting).SetProperty(w => w.OfferCalendarId, (Guid?)null).SetProperty(w => w.OfferStart, (DateTimeOffset?)null)
                    .SetProperty(w => w.OfferExpiresAt, (DateTimeOffset?)null).SetProperty(w => w.ChangedAt, now), cancellationToken) == 0)
            throw new VisitTransitionException("changed");
        await db.SlotHolds.Where(h => h.HeldBy == id).ExecuteDeleteAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.WaitlistDecline, SchedulingAudit.WaitlistResource, SchedulingAudit.Treatment, id, entry.PatientId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ViewAsync(id, cancellationToken);
    }

    public async Task<AppointmentView> AcceptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await FindAsync(id, cancellationToken);
        var now = context.Now;
        if (entry.State != WaitlistState.Offered || entry.OfferCalendarId is not { } calendar) throw new SlotUnavailableException("no_offer");
        var hold = await db.SlotHolds.AsNoTracking().SingleOrDefaultAsync(h => h.HeldBy == id, cancellationToken);
        if (entry.OfferExpiresAt <= now || hold is null || hold.ExpiresAt <= now) throw new SlotUnavailableException("offer_expired");
        var patient = (await context.PatientsAsync([entry.PatientId], cancellationToken)).GetValueOrDefault(entry.PatientId) is { } p
            ? p
            : throw new SchedulingValidationException([new("patientId", "unknown")]);
        var (_, zone) = await context.FacilityAsync(entry.FacilityId, cancellationToken);

        var visit = await transaction.RunAsync([(calendar, ScheduleRules.LocalDate(hold.Start, zone))], async () =>
        {
            // The offer proved the slot was free; the template or a booking may have changed since.
            if (await booking.BookableEndAsync(entry.FacilityId, calendar, hold.Start, hold.VisitType, zone, cancellationToken) != hold.End)
                throw new SlotUnavailableException("slot_unavailable");
            var draft = BookingService.Draft(entry.FacilityId, calendar, CalendarKind.Doctor, entry.PatientId, entry.VisitType, hold.Start, hold.End, entry.ReasonCategory, null, BookingChannel.Phone, now);
            if (await db.Waitlist.Where(w => w.Id == id && w.State == WaitlistState.Offered && w.OfferExpiresAt > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.State, WaitlistState.Booked).SetProperty(w => w.AppointmentId, draft.Id).SetProperty(w => w.ChangedAt, now), cancellationToken) == 0)
                throw new VisitTransitionException("changed");
            await db.SlotHolds.Where(h => h.HeldBy == id).ExecuteDeleteAsync(cancellationToken);
            var created = await booking.CreateAsync(draft, SchedulingAudit.Book, cancellationToken);
            await context.AuditAsync(SchedulingAudit.WaitlistAccept, SchedulingAudit.WaitlistResource, SchedulingAudit.Treatment, id, entry.PatientId, cancellationToken);
            return created;
        }, cancellationToken);
        return await booking.ViewAsync(visit, patient, now);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await FindAsync(id, cancellationToken);
        var now = context.Now;
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Waitlist.Where(w => w.Id == id && (w.State == WaitlistState.Waiting || w.State == WaitlistState.Offered))
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.State, WaitlistState.Removed).SetProperty(w => w.ChangedAt, now), cancellationToken) == 0)
            throw new VisitTransitionException("invalid_transition");
        await db.SlotHolds.Where(h => h.HeldBy == id).ExecuteDeleteAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.WaitlistRemove, SchedulingAudit.WaitlistResource, SchedulingAudit.Treatment, id, entry.PatientId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>The entry, after checking the caller may book at its facility (another tenant's id is "not found": filters and row-level security).</summary>
    async Task<WaitlistEntry> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.Waitlist.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, cancellationToken) ?? throw new WaitlistNotFoundException();
        await context.RequireAsync(entry.FacilityId, SchedulingAccess.CanBook, cancellationToken);
        return entry;
    }

    async Task<WaitlistView> ViewAsync(Guid id, CancellationToken cancellationToken)
    {
        var w = await db.Waitlist.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        var p = (await context.PatientsAsync([w.PatientId], cancellationToken)).GetValueOrDefault(w.PatientId);
        var names = (await context.CalendarsAsync(w.FacilityId, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        string Name(Guid g) => names.GetValueOrDefault(g) ?? "";
        var now = context.Now;
        var offered = w.State == WaitlistState.Offered && w.OfferExpiresAt > now && w.OfferCalendarId is { } oc && w.OfferStart is { } os ? new SlotMatch(oc, Name(oc), os) : null;
        return Of(w, p?.FullName ?? "", p?.Mrn ?? "", w.CalendarId is { } c ? Name(c) : null, offered, null, now);
    }

    static WaitlistView Of(WaitlistEntry w, string name, string mrn, string? doctor, SlotMatch? offered, SlotMatch? match, DateTimeOffset now) => new(
        w.Id, w.PatientId, name, mrn, w.CalendarId, doctor, w.VisitType, w.ReasonCategory,
        [.. Enum.GetValues<DayOfWeek>().Where(d => (w.Days & WaitlistEntry.DayBit(d)) != 0)], w.From, w.Until,
        w.Open(now) ? WaitlistState.Waiting : w.State, w.RequestedAt, offered, offered is null ? null : w.OfferExpiresAt, match);
}
