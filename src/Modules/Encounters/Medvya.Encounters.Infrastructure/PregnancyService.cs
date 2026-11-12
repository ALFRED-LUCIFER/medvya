using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Identity.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The pregnancy record (MDV-P2-034). Clinical access first, then the write and its audit event (with the patient, never a date or a count) in
/// one transaction. Dates are the facility's: today, gestation and the contacts' visits are all read in its time zone.
/// </summary>
public sealed class PregnancyService(
    EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IStaffUsers staff, IFacilityDirectory facilities)
    : IPregnancies
{
    public const string StartAction = "pregnancy.start", DateAction = "pregnancy.date", CloseAction = "pregnancy.close", CounselAction = "pregnancy.danger-signs",
        RuleOutAction = "pregnancy.ruled-out", ReadingsAction = "pregnancy.readings";
    static readonly Access[] Doctors = [Access.Sign], Clinicians = [Access.Full];

    public async Task<PregnancyStatus> StatusAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(facilityId, cancellationToken);
        var today = On(clock.GetUtcNow(), zone);
        var open = await OpenAsync(patientId, cancellationToken);
        var ruledOut = open is not null ? null : await db.PregnancyRuledOuts.AsNoTracking().Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.RecordedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (ruledOut is not null && !PregnancyRules.RuledOutHolds(On(ruledOut.RecordedAt, zone), today)) ruledOut = null;
        var names = await staff.DisplayNamesAsync([.. new[] { open?.StartedBy, open?.DangerSignsCounselledBy, ruledOut?.RecordedBy }.OfType<Guid>().Distinct()], cancellationToken);
        if (open is null) return new(null, ruledOut is null ? null : new(names.GetValueOrDefault(ruledOut.RecordedBy), ruledOut.RecordedAt));

        var visits = (await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId).Select(e => new { e.Id, e.StartedAt }).ToListAsync(cancellationToken))
            .Select(e => (e.Id, On(e.StartedAt, zone)));
        return new(new(open.Id, open.Lmp, open.ScanDate, open.ScanGestationDays, open.DueDate, open.DatedBy, open.DatingReason, open.PreviousDueDate,
            PregnancyRules.GestationDays(open.DueDate, today), open.Gravida, open.Para, open.Abortions, open.Living, names.GetValueOrDefault(open.StartedBy), open.StartedAt,
            [.. PregnancyRules.Contacts(open.DueDate, today, visits).Select(c => new AntenatalContactView(c.Week, c.State, c.VisitId))], Enum.GetValues<DangerSign>(),
            open.DangerSignsCounselledBy is { } c ? names.GetValueOrDefault(c) : null, open.DangerSignsCounselledAt, PregnancyRules.SignOff is not null), null);
    }

    public async Task<PregnancyStatus> StartAsync(Guid patientId, Guid facilityId, StartPregnancy command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Doctors, cancellationToken);
        var today = On(clock.GetUtcNow(), await ZoneAsync(facilityId, cancellationToken));
        var errors = Dates(new(command.Lmp, command.ScanDate, command.ScanWeeks, command.ScanDays), today, out var scanDays);
        Count(errors, "gravida", command.Gravida, 1);
        Count(errors, "para", command.Para, 0);
        Count(errors, "abortions", command.Abortions, 0);
        Count(errors, "living", command.Living, 0);
        // Gravida counts this pregnancy, so the earlier births and losses are at most one fewer.
        if (command is { Gravida: { } g, Para: { } p, Abortions: { } a } && errors.Count == 0 && p + a > g - 1) errors.Add(("para", "more_than_gravida"));
        if (errors.Count > 0) throw new PregnancyValidationException(errors);
        if (await OpenAsync(patientId, cancellationToken) is not null) throw new PregnancyConflictException("already_open");

        var (due, method, reason) = PregnancyRules.Date(command.Lmp, command.ScanDate, scanDays);
        var (now, me) = Now();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.PregnancyEpisodes.Add(new()
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, Lmp = command.Lmp, ScanDate = command.ScanDate, ScanGestationDays = scanDays,
            DueDate = due, DatedBy = method, DatingReason = reason, Gravida = command.Gravida, Para = command.Para, Abortions = command.Abortions, Living = command.Living,
            StartedBy = me, StartedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new PregnancyConflictException("already_open"); // a second start at the same moment: the partial unique index decides
        }
        await AuditAsync(StartAction, patientId, now, me, tx, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await StatusAsync(patientId, facilityId, cancellationToken);
    }

    public async Task<PregnancyStatus> DateAsync(Guid patientId, Guid facilityId, PregnancyDates command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Doctors, cancellationToken);
        var today = On(clock.GetUtcNow(), await ZoneAsync(facilityId, cancellationToken));
        var errors = Dates(command, today, out var scanDays);
        if (errors.Count > 0) throw new PregnancyValidationException(errors);
        return await ChangeOpenAsync(patientId, facilityId, DateAction, (row, _, _) =>
        {
            var (due, method, reason) = PregnancyRules.Date(command.Lmp, command.ScanDate, scanDays);
            if (due != row.DueDate) row.PreviousDueDate = row.DueDate; // the date it replaces is kept, never lost
            (row.Lmp, row.ScanDate, row.ScanGestationDays, row.DueDate, row.DatedBy, row.DatingReason) = (command.Lmp, command.ScanDate, scanDays, due, method, reason);
        }, cancellationToken);
    }

    public async Task<PregnancyStatus> CloseAsync(Guid patientId, Guid facilityId, ClosePregnancy command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Doctors, cancellationToken);
        if (command.Outcome is not { } outcome || !Enum.IsDefined(outcome)) throw new PregnancyValidationException([("outcome", command.Outcome is null ? "required" : "invalid")]);
        return await ChangeOpenAsync(patientId, facilityId, CloseAction, (row, now, me) => (row.Outcome, row.ClosedBy, row.ClosedAt) = (outcome, me, now), cancellationToken);
    }

    public async Task<PregnancyStatus> CounselDangerSignsAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Clinicians, cancellationToken);
        return await ChangeOpenAsync(patientId, facilityId, CounselAction, (row, now, me) => (row.DangerSignsCounselledBy, row.DangerSignsCounselledAt) = (me, now), cancellationToken);
    }

    public async Task<PregnancyStatus> RuleOutAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Clinicians, cancellationToken);
        if (await OpenAsync(patientId, cancellationToken) is not null) throw new PregnancyConflictException("pregnancy_open");
        var (now, me) = Now();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.PregnancyRuledOuts.Add(new() { Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, FacilityId = facilityId, RecordedBy = me, RecordedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(RuleOutAction, patientId, now, me, tx, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await StatusAsync(patientId, facilityId, cancellationToken);
    }

    public async Task<PregnancyStatus> RecordReadingsAsync(Guid patientId, Guid facilityId, PregnancyReadings command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Clinicians, cancellationToken);
        List<(string, string)> errors = [];
        var readings = new List<(ObservationCode Code, decimal Value)>();
        foreach (var (field, code, value) in new[] { ("fundalHeightCm", ObservationCode.FundalHeight, command.FundalHeightCm), ("fetalHeartRate", ObservationCode.FetalHeartRate, command.FetalHeartRate) })
        {
            if (value is not { } v) continue; // a blank is unknown, not zero
            var (min, max) = PregnancyRules.Possible(code);
            if (v < min || v > max) errors.Add((field, "implausible"));
            else if (decimal.Round(v, 1) != v) errors.Add((field, "too_precise"));
            else readings.Add((code, v));
        }
        if (errors.Count == 0 && readings.Count == 0) errors.Add(("readings", "required"));
        if (errors.Count > 0) throw new PregnancyValidationException(errors);
        if (await OpenAsync(patientId, cancellationToken) is null) throw new PregnancyConflictException("no_open_pregnancy");
        var visit = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId && e.FacilityId == facilityId && e.Status == EncounterStatus.Active)
            .OrderByDescending(e => e.StartedAt).FirstOrDefaultAsync(cancellationToken) ?? throw new PregnancyConflictException("no_open_visit");

        var (now, me) = Now();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // The same OBSERVATION row as a nurse's vital at intake; the chart flags it against ReferenceRanges.
        db.Observations.AddRange(readings.Select(r => new Observation
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, FacilityId = facilityId, PatientId = patientId, AppointmentId = visit.AppointmentId, Code = r.Code,
            ValueHundredths = (int)(r.Value * 100), Source = ChartSource.ClinicianVerified, RecordedBy = me, VerifiedBy = me, TakenAt = now,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(ReadingsAction, patientId, now, me, tx, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await StatusAsync(patientId, facilityId, cancellationToken);
    }

    async Task<PregnancyStatus> ChangeOpenAsync(Guid patientId, Guid facilityId, string action, Action<PregnancyEpisode, DateTimeOffset, Guid> change, CancellationToken cancellationToken)
    {
        var (now, me) = Now();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.PregnancyEpisodes.Where(p => p.PatientId == patientId && p.Outcome == null).FirstOrDefaultAsync(cancellationToken)
            ?? throw new PregnancyConflictException("no_open_pregnancy");
        change(row, now, me);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(action, patientId, now, me, tx, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await StatusAsync(patientId, facilityId, cancellationToken);
    }

    /// <summary>The dates' errors; <paramref name="scanDays"/> is the gestation at the scan in days, when a scan is given.</summary>
    static List<(string, string)> Dates(PregnancyDates d, DateOnly today, out int? scanDays)
    {
        List<(string, string)> errors = [];
        scanDays = null;
        var earliest = today.AddDays(-PregnancyRules.MaxGestationDays);
        if (d.Lmp is { } lmp) errors.AddRange(lmp > today ? [("lmp", "in_future")] : lmp < earliest ? [("lmp", "too_early")] : []);
        var scan = d.ScanDate is not null || d.ScanWeeks is not null || d.ScanDays is not null;
        if (d.Lmp is null && !scan) errors.Add(("lmp", "required"));
        if (!scan) return errors;
        if (d.ScanWeeks is not { } weeks) errors.Add(("scanWeeks", "required"));
        else if (d.ScanDays is { } days and (< 0 or > 6)) errors.Add(("scanDays", "invalid"));
        else if (weeks * 7 + (d.ScanDays ?? 0) is var total && (total < 1 || total > PregnancyRules.MaxGestationDays)) errors.Add(("scanWeeks", "invalid"));
        else scanDays = weeks * 7 + (d.ScanDays ?? 0);
        if (d.ScanDate is not { } on) errors.Add(("scanDate", "required"));
        else if (on > today) errors.Add(("scanDate", "in_future"));
        else if (d.Lmp is { } l && on < l) errors.Add(("scanDate", "before_lmp"));
        else if (scanDays is { } sd && on.AddDays(-sd) < earliest) errors.Add(("scanDate", "too_early"));
        return errors;
    }

    static void Count(List<(string, string)> errors, string field, int? value, int min)
    {
        if (value is { } v && (v < min || v > PregnancyRules.MaxCount)) errors.Add((field, "invalid"));
    }

    Task<PregnancyEpisode?> OpenAsync(Guid patientId, CancellationToken cancellationToken) =>
        db.PregnancyEpisodes.AsNoTracking().Where(p => p.PatientId == patientId && p.Outcome == null).FirstOrDefaultAsync(cancellationToken);

    async Task<TimeZoneInfo> ZoneAsync(Guid facilityId, CancellationToken cancellationToken) =>
        TimeZoneInfo.FindSystemTimeZoneById((await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.")).TimeZone);

    static DateOnly On(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    (DateTimeOffset, Guid) Now() => (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");

    Task AuditAsync(string action, Guid patientId, DateTimeOffset now, Guid me, IDbContextTransaction tx, CancellationToken cancellationToken) =>
        audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = me, Action = action, ResourceType = "patient", ResourceId = patientId, PatientId = patientId,
            Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
}
