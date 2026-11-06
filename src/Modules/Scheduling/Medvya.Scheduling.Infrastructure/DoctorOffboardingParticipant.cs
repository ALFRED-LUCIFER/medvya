using System.Data.Common;
using System.Globalization;
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

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// A doctor left the practice (MDV-P1-009): their future booked visits become "needs rebooking" for reception to move (slots freed, audited,
/// board told after the commit). It runs inside the offboarding transaction on its connection, so the tenant's row-level security applies and
/// the move commits or rolls back with the membership change. The caller has already authorised staff administration. Takes the same
/// per-doctor-day app locks as every booking change, so a booking cannot slip in between.
/// </summary>
public sealed class DoctorOffboardingParticipant(
    ICurrentTenant tenant, ICurrentUser user, IFacilityDirectory facilities, IAuditLog audit, IOptions<SchedulingOptions> options,
    TimeProvider clock, IBoardNotifier notifier, ILogger<DoctorOffboardingParticipant> log) : IStaffOffboardingParticipant
{
    public async Task<Func<Task>?> OffboardedAsync(Guid doctorUserId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var (now, me, tenantId) = (clock.GetUtcNow(), user.UserId, tenant.TenantId ?? throw new InvalidOperationException("No tenant context."));
        await using var db = new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseSqlServer(transaction.Connection!).Options, tenant);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        Task<List<Appointment>> FutureAsync() => db.Appointments.AsNoTracking()
            .Where(a => a.CalendarId == doctorUserId && a.Start >= now && a.State == AppointmentState.Booked && !a.Unscheduled).ToListAsync(cancellationToken);

        var zones = new Dictionary<Guid, TimeZoneInfo>();
        var days = new SortedSet<(Guid FacilityId, DateOnly Date)>();
        foreach (var a in await FutureAsync())
        {
            if (!zones.TryGetValue(a.FacilityId, out var zone))
                zones[a.FacilityId] = zone = TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(a.FacilityId, cancellationToken)
                    ?? throw new InvalidOperationException("A visit's facility is not in this tenant.")).TimeZone);
            days.Add((a.FacilityId, ScheduleRules.LocalDate(a.Start, zone)));
        }
        foreach (var date in days.Select(d => d.Date).Distinct().Order())
            await DoctorDayTransaction.LockAsync(db, string.Create(CultureInfo.InvariantCulture, $"doctor:{doctorUserId}:{date:yyyy-MM-dd}"), options.Value.LockTimeout, cancellationToken);

        var affected = await FutureAsync(); // read again under the locks
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
            await audit.WriteAsync(new AuditEvent
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = me, Action = SchedulingAudit.NeedsRebooking, ResourceType = SchedulingAudit.AppointmentResource,
                ResourceId = a.Id, PatientId = a.PatientId, Purpose = SchedulingAudit.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = now,
            }, transaction, cancellationToken);
        return affected.Count == 0 ? null : async () =>
        {
            foreach (var a in affected) await FrontDeskService.NotifyAsync(notifier, log, new(a.TenantId, a.FacilityId, a.Id, AppointmentState.NeedsRebooking, now));
        };
    }
}
