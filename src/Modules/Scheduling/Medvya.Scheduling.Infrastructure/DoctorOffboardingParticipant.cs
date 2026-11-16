using System.Data.Common;
using System.Text.Json;
using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// A doctor left a facility of the practice (MDV-P1-009): their future booked visits there (not at the practice's other sites, #877) become "needs rebooking" (slots freed, audited, queued
/// reminders cancelled; the patient is asked to rebook only if the admin chose to offer new slots),
/// board told after the commit). It runs inside the offboarding transaction on its connection, so the tenant's row-level security applies and
/// the move commits or rolls back with the membership change. The caller has already authorised staff administration. The doctor's calendar
/// lock is held exclusive (every booking change holds it shared) from before the roster's transaction to after its commit (<see cref="HoldAsync"/>),
/// so no booking of the doctor is in flight during it, and one that waited sees the membership offboarded (it re-checks under the lock).
/// </summary>
public sealed class DoctorOffboardingParticipant(
    ICurrentTenant tenant, ICurrentUser user, IFacilityDirectory facilities, IAuditLog audit, IOptions<SchedulingOptions> options,
    TimeProvider clock, IBoardNotifier notifier, ILogger<DoctorOffboardingParticipant> log, IEnumerable<IRebookingParticipant> rebooking) : IStaffOffboardingParticipant
{
    public async Task<IAsyncDisposable> HoldAsync(Guid userId, DbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var key = DoctorDayTransaction.DoctorKey(tenant.TenantId ?? throw new InvalidOperationException("No tenant context."), userId);
        // A session lock, taken outside any transaction: an advisory xact lock would be taken after the serializable snapshot and wait in vain.
        await using (var take = connection.CreateCommand())
        {
            take.CommandText = "SELECT set_config('lock_timeout', @timeout, false); SELECT pg_advisory_lock(hashtextextended(@key, 0));";
            take.Parameters.Add(new NpgsqlParameter("timeout", ((int)options.Value.LockTimeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            take.Parameters.Add(new NpgsqlParameter("key", key));
            try { await take.ExecuteNonQueryAsync(cancellationToken); }
            finally
            {
                await using var reset = connection.CreateCommand();
                reset.CommandText = "RESET lock_timeout";
                await reset.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        return new SessionLock(connection, key);
    }

    /// <summary>Releases the session lock (closing the connection would too: the pool's reset discards it).</summary>
    sealed class SessionLock(DbConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var release = connection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@key, 0))";
            release.Parameters.Add(new NpgsqlParameter("key", key));
            await release.ExecuteNonQueryAsync();
        }
    }

    public async Task<OffboardingShare> OffboardedAsync(Guid doctorUserId, Guid facilityId, bool offerRebooking, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var (now, me, tenantId) = (clock.GetUtcNow(), user.UserId, tenant.TenantId ?? throw new InvalidOperationException("No tenant context."));
        await using var db = new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        Task<List<Appointment>> FutureAsync() => db.Appointments.AsNoTracking()
            .Where(a => a.CalendarId == doctorUserId && a.FacilityId == facilityId && a.Start >= now && a.State == AppointmentState.Booked && !a.Unscheduled).ToListAsync(cancellationToken);

        TimeZoneInfo? zone = null; // every visit read is at this one facility
        async Task<TimeZoneInfo> ZoneAsync() => zone ??= TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(facilityId, cancellationToken)
            ?? throw new InvalidOperationException("A visit's facility is not in this tenant.")).TimeZone);
        var days = new SortedSet<DateOnly>();
        foreach (var a in await FutureAsync()) days.Add(ScheduleRules.LocalDate(a.Start, await ZoneAsync()));
        foreach (var date in days)
            await DoctorDayTransaction.LockAsync(db, DoctorDayTransaction.DoctorDayKey(tenantId, doctorUserId, date), options.Value.LockTimeout, cancellationToken);

        var affected = await FutureAsync(); // read again under the locks
        foreach (var a in affected)
        {
            if (await db.Appointments.Where(x => x.Id == a.Id && x.State == AppointmentState.Booked)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, AppointmentState.NeedsRebooking).SetProperty(x => x.ChangedBy, me).SetProperty(x => x.ChangedAt, now), cancellationToken) == 0)
                throw new VisitTransitionException("changed");
            await db.ScheduleSlots.Where(s => s.AppointmentId == a.Id).ExecuteDeleteAsync(cancellationToken);
            db.StatusEvents.Add(new VisitStatusEvent { Id = Guid.CreateVersion7(), TenantId = a.TenantId, AppointmentId = a.Id, State = AppointmentState.NeedsRebooking, ActorUserId = me, OccurredAt = now });
            // Always written: it cancels the visit's queued reminders. The admin chose whether the patient is asked to rebook (#225) or called by reception (quiet).
            db.Outbox.Add(new OutboxEvent
            {
                Id = Guid.CreateVersion7(), TenantId = a.TenantId, Type = "AppointmentNeedsRebooking.v1", AggregateId = a.Id, OccurredAt = now,
                Payload = JsonSerializer.Serialize(new { appointmentId = a.Id, facilityId = a.FacilityId, calendarId = a.CalendarId, patientId = a.PatientId, start = a.Start, quiet = !offerRebooking }),
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        if (affected.Count > 0) // the desk's notification-centre item (MDV-P1-034), committed with the offboarding
        {
            var first = ScheduleRules.LocalDate(affected.Min(a => a.Start), await ZoneAsync());
            foreach (var p in rebooking) await p.NeedsRebookingAsync(facilityId, doctorUserId, first, affected.Count, transaction, cancellationToken);
        }
        foreach (var a in affected)
            await audit.WriteAsync(new AuditEvent
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = me, Action = SchedulingAudit.NeedsRebooking, ResourceType = SchedulingAudit.AppointmentResource,
                ResourceId = a.Id, PatientId = a.PatientId, Purpose = SchedulingAudit.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = now,
            }, transaction, cancellationToken);
        // Patients already checked in for this doctor (waiting or in consultation) stay on the board: reception reassigns them there.
        var open = await db.Appointments.CountAsync(a => a.CalendarId == doctorUserId && a.FacilityId == facilityId
            && (a.State == AppointmentState.CheckedIn || a.State == AppointmentState.WithNurse || a.State == AppointmentState.ReadyForDoctor || a.State == AppointmentState.InConsult), cancellationToken);
        return new(affected.Count == 0 ? null : async () =>
        {
            foreach (var a in affected) await FrontDeskService.NotifyAsync(notifier, log, new(a.TenantId, a.FacilityId, a.Id, AppointmentState.NeedsRebooking, now));
        }, open);
    }
}
