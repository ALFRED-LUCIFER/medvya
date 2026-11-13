using Medvya.Scheduling.Application;
using Medvya.Scheduling.Domain;
using Medvya.Scheduling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Scheduling.Infrastructure;

/// <summary>
/// Pilot metrics (MDV-P1-041) from what scheduling already writes: check-in status events (timed or not measured), visit states and the database's
/// double-booking refusals. Aggregates only; patient ids are used to tell returning patients and never leave this class.
/// </summary>
public sealed class PilotMetricsService(SchedulingDbContext db, SchedulingContext context) : IPilotMetrics
{
    const int MaxMethod = 200;

    public async Task<PilotMetrics> GetAsync(Guid facilityId, int days, CancellationToken cancellationToken = default)
    {
        await context.RequireAsync(facilityId, SchedulingAccess.CanConfigure, cancellationToken);
        var until = context.Now;
        var since = until.AddDays(-Math.Clamp(days, 1, 90));

        var checkIns = await (
            from e in db.StatusEvents
            join a in db.Appointments on e.AppointmentId equals a.Id
            where e.State == AppointmentState.CheckedIn && e.OccurredAt >= since && e.OccurredAt < until && a.FacilityId == facilityId
            select new { a.PatientId, a.Start, e.CheckInMs }).ToListAsync(cancellationToken);
        // Not measured (no start, or an impossible time) is left out and counted apart, never read as zero (MDV-P1-106).
        var arrivals = checkIns.Count;
        checkIns = [.. checkIns.Select(c => c with { CheckInMs = PilotMetricRules.TimedMs(c.CheckInMs) }).Where(c => c.CheckInMs != null)];
        // Returning: the patient came to an earlier visit at the practice (any location) before this one.
        var patientIds = checkIns.Select(c => c.PatientId).Distinct().ToList();
        var firstVisit = await db.Appointments.Where(a => patientIds.Contains(a.PatientId) && Attended.Contains(a.State))
            .GroupBy(a => a.PatientId).Select(g => new { g.Key, First = g.Min(a => a.Start) }).ToDictionaryAsync(x => x.Key, x => x.First, cancellationToken);
        var returning = checkIns.Where(c => firstVisit.TryGetValue(c.PatientId, out var first) && first < c.Start).Select(c => c.CheckInMs!.Value / 1000d).ToList();
        var checkIn = new CheckInMetric(PilotMetricRules.Median(returning), returning.Count, PilotMetricRules.Median(checkIns.Select(c => c.CheckInMs!.Value / 1000d)), checkIns.Count,
            PilotMetricRules.CheckInTargetSeconds, arrivals - checkIns.Count);

        // Walk-ins never no-show; a booked visit still waiting for its outcome counts in neither number.
        var states = await db.Appointments.Where(a => a.FacilityId == facilityId && !a.WalkIn && a.Start >= since && a.Start < until)
            .GroupBy(a => a.State).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var noShows = states.Where(s => s.Key == AppointmentState.NoShow).Sum(s => s.Count);
        var attended = states.Where(s => Attended.Contains(s.Key)).Sum(s => s.Count);

        var refusals = await db.BookingConflicts.CountAsync(c => c.FacilityId == facilityId && c.OccurredAt >= since && c.OccurredAt < until, cancellationToken);
        var baselines = (await db.MetricBaselines.AsNoTracking().Where(b => b.FacilityId == facilityId).ToListAsync(cancellationToken))
            .GroupBy(b => b.Metric).Select(g => g.MaxBy(b => b.RecordedAt)!).OrderBy(b => b.Metric).Select(View).ToList();
        return new(since, until, checkIn, new(noShows, attended, PilotMetricRules.Percent(noShows, noShows + attended)), refusals, baselines);
    }

    public async Task<BaselineView> RecordBaselineAsync(RecordBaseline command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var method = ScheduleService.Clean(command.Method);
        var errors = new List<ValidationError>();
        if (command.Metric is not { } metric || !Enum.IsDefined(metric)) errors.Add(new("metric", "required"));
        if (command.Value is not { } value || value < 0) errors.Add(new("value", "required"));
        else if (command.Metric is PilotMetric.NoShowPercent or PilotMetric.ReminderDeliveryPercent && value > 100) errors.Add(new("value", "out_of_range")); // a percentage
        if (method is null) errors.Add(new("method", "required"));
        else if (method.Length > MaxMethod) errors.Add(new("method", "too_long"));
        if (command.FromDate is null) errors.Add(new("fromDate", "required"));
        if (command.ToDate is null) errors.Add(new("toDate", "required"));
        else if (command.FromDate > command.ToDate) errors.Add(new("toDate", "before_from"));
        SchedulingValidationException.ThrowIfAny(errors);
        await context.RequireAsync(command.FacilityId, SchedulingAccess.CanConfigure, cancellationToken);

        var baseline = new MetricBaseline
        {
            Id = Guid.CreateVersion7(), TenantId = context.TenantId, FacilityId = command.FacilityId, Metric = command.Metric!.Value, Value = command.Value!.Value,
            Method = method!, FromDate = command.FromDate!.Value, ToDate = command.ToDate!.Value, RecordedBy = context.UserId, RecordedAt = context.Now,
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.MetricBaselines.Add(baseline);
        await db.SaveChangesAsync(cancellationToken);
        await context.AuditAsync(SchedulingAudit.BaselineRecord, SchedulingAudit.MetricsResource, SchedulingAudit.Operations, baseline.Id, null, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(baseline);
    }

    /// <summary>The patient came: checked in and anywhere further along the board.</summary>
    static readonly AppointmentState[] Attended = [.. VisitFlow.BoardStates.Where(s => s != AppointmentState.Booked)];

    static BaselineView View(MetricBaseline b) => new(b.Metric, b.Value, b.Method, b.FromDate, b.ToDate, b.RecordedAt);
}
