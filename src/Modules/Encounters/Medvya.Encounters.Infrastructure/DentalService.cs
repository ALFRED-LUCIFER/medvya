using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The dental chart and plan (MDV-P8-011, MDV-P8-012); see <see cref="IDental"/>. Input is checked against <see cref="DentalRules"/>; findings and
/// acceptances are only ever inserted, plan items only ever move. Each write and its audit event commit together.
/// </summary>
public sealed class DentalService(
    EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, ICatalogue catalogue,
    IPatientDirectory patients, IFacilityDirectory facilities, IStaffUsers staff) : IDental, IDentalCharges
{
    public const string ReadAction = "patient.dental.read", FindingAction = "patient.dental.finding", PlanAction = "patient.dental.plan",
        MoveAction = "patient.dental.plan_move", AcceptAction = "patient.dental.estimate_accept", PdfAction = "patient.dental.pdf";
    static readonly Access[] Treats = [Access.Read, Access.Limited, Access.Full, Access.Sign];
    static readonly Access[] Dentists = [Access.Sign];

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");

    public async Task<DentalChartView> ChartAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, Treats, cancellationToken);
        await audit.WriteAsync(Event(ReadAction, patientId), cancellationToken);
        var open = await db.Encounters.AsNoTracking().Where(e => e.PatientId == patientId && e.FacilityId == facilityId && e.Status == EncounterStatus.Active && e.LeadClinicianId == Me)
            .OrderByDescending(e => e.StartedAt).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
        return await ReadAsync(patientId, cancellationToken) with { OpenEncounterId = open };
    }

    async Task<DentalChartView> ReadAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var history = await db.DentalFindings.AsNoTracking().Where(f => f.PatientId == patientId)
            .OrderByDescending(f => f.RecordedAt).ThenByDescending(f => f.Id).Take(DentalRules.MaxHistory).ToListAsync(cancellationToken);
        var views = history.Select(View).ToList();
        var teeth = views.GroupBy(f => f.Tooth).Select(g => g.First()).OrderBy(f => f.Tooth, StringComparer.Ordinal).ToList();
        return new DentalChartView(teeth, views, await PlanAsync(patientId, cancellationToken));
    }

    async Task<DentalPlanView> PlanAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var items = await db.DentalPlanItems.AsNoTracking().Where(i => i.PatientId == patientId)
            .OrderBy(i => i.Phase).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var accepted = await db.DentalEstimateAcceptances.AsNoTracking().Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AcceptedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(cancellationToken);
        return new DentalPlanView([.. items.Select(View)], DentalRules.EstimateTotal(items), items.FirstOrDefault()?.Currency,
            accepted is null ? null : new(accepted.Id, accepted.AcceptedByName, accepted.Method, accepted.TotalMinor, accepted.Currency, accepted.AcceptedAt, accepted.RecordedBy),
            accepted is not null && accepted.ItemsKey == DentalRules.EstimateKey(items));
    }

    public async Task<ToothFindingView?> RecordFindingAsync(Guid encounterId, Guid facilityId, RecordToothFinding command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken) is not { } encounter || encounter.FacilityId != facilityId) return null;
        await clinical.RequireAsync(encounter.PatientId, facilityId, Capability.ClinicalNotes, Dentists, cancellationToken);
        var errors = DentalRules.Validate(command.Tooth?.Trim(), command.Condition, command.Surfaces, command.Note);
        if (encounter.Status != EncounterStatus.Active) errors.Add(("encounter", "ended")); // a finding belongs to an open visit; later ones are a new visit's
        if (errors.Count > 0) throw new DentalValidationException(errors);
        var row = new DentalFinding
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = encounter.PatientId, FacilityId = facilityId, EncounterId = encounter.Id,
            Tooth = command.Tooth!.Trim(), Condition = command.Condition!.Value, Surfaces = DentalRules.Surfaces(command.Surfaces) is { Length: > 0 } s ? s : null,
            Note = command.Note?.Trim() is { Length: > 0 } note ? note : null, RecordedBy = Me, RecordedAt = clock.GetUtcNow(),
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DentalFindings.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(FindingAction, encounter.PatientId), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(row);
    }

    public async Task<IReadOnlyList<DentalServiceOption>> ServicesAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Dentists, cancellationToken);
        return [.. (await catalogue.ListAsync(includeArchived: false, cancellationToken)).Where(s => s.Category != ServiceCategory.Other)
            .OrderBy(s => s.Code ?? s.Name, StringComparer.Ordinal).Select(s => new DentalServiceOption(s.Id, s.Name, s.Code, s.DurationMinutes, s.ClinicPriceMinor, s.Currency))];
    }

    public async Task<DentalPlanItemView> AddPlanItemAsync(Guid patientId, Guid facilityId, AddDentalPlanItem command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Dentists, cancellationToken);
        var tooth = command.Tooth?.Trim() is { Length: > 0 } t ? t : null;
        var errors = DentalRules.ValidatePlanItem(tooth, command.Surfaces, command.Phase);
        if (command.ServiceId is null) errors.Add(("serviceId", "required"));
        if (errors.Count > 0) throw new DentalValidationException(errors);
        // The estimate is the clinic's default price now (shortcut: a dentist's own price, MDV-P4-002, applies on the bill, not the estimate; show it here when a clinic asks).
        var service = (await catalogue.ListAsync(includeArchived: false, cancellationToken)).SingleOrDefault(s => s.Id == command.ServiceId)
            ?? throw new DentalValidationException([("serviceId", "unknown")]);
        var price = await catalogue.ResolvePriceAsync(service.Id, null, clock.GetUtcNow(), cancellationToken) ?? throw new DentalValidationException([("serviceId", "no_price")]);
        var now = clock.GetUtcNow();
        var row = new DentalPlanItem
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, FacilityId = facilityId, ServiceId = service.Id, ServiceName = service.Name,
            Tooth = tooth, Surfaces = DentalRules.Surfaces(command.Surfaces) is { Length: > 0 } s ? s : null, Phase = command.Phase!.Value,
            EstimateMinor = price.AmountMinor, Currency = price.Currency, Status = DentalPlanStatus.Planned, CreatedBy = Me, CreatedAt = now, ChangedBy = Me, ChangedAt = now,
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DentalPlanItems.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(PlanAction, patientId, row.Id), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return View(row);
    }

    public async Task<DentalPlanItemView?> MovePlanItemAsync(Guid patientId, Guid facilityId, Guid itemId, MoveDentalPlanItem command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Dentists, cancellationToken);
        if (await db.DentalPlanItems.SingleOrDefaultAsync(i => i.Id == itemId && i.PatientId == patientId, cancellationToken) is not { } item) return null;
        var errors = new List<(string, string)>();
        if (command.To is not { } to || !Enum.IsDefined(to) || !DentalRules.CanMove(item.Status, to)) errors.Add(("to", "not_allowed"));
        var reason = command.Reason?.Trim() is { Length: > 0 } r ? r : null;
        if (command.To == DentalPlanStatus.Declined && reason is null) errors.Add(("reason", "required"));
        if (reason?.Length > DentalPlanItem.MaxReason) errors.Add(("reason", "too_long"));
        Encounter? encounter = null;
        if (command.To is DentalPlanStatus.InProgress or DentalPlanStatus.Done)
        {
            // Started and finished in the patient's open consultation, so a done item is billed to the visit it was done in.
            if (command.EncounterId is not { } encounterId) errors.Add(("encounterId", "required"));
            else if ((encounter = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken)) is null
                || encounter.PatientId != patientId || encounter.FacilityId != facilityId) errors.Add(("encounterId", "invalid"));
            else if (encounter.Status != EncounterStatus.Active) errors.Add(("encounterId", "ended"));
        }
        if (errors.Count > 0) throw new DentalValidationException(errors);
        if (command.Version is { } read && read != item.Version) throw new DentalConflictException();

        var now = clock.GetUtcNow();
        item.Status = command.To!.Value;
        if (reason is not null) item.Reason = reason;
        if (item.Status == DentalPlanStatus.Done) (item.DoneAppointmentId, item.DoneAt) = (encounter!.AppointmentId, now);
        (item.ChangedBy, item.ChangedAt) = (Me, now);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(Event(MoveAction, patientId, item.Id), tx.GetDbTransaction(), cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DentalConflictException();
        }
        return View(item);
    }

    public async Task<DentalAcceptanceView> AcceptEstimateAsync(Guid patientId, Guid facilityId, AcceptDentalEstimate command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.ClinicalNotes, Dentists, cancellationToken);
        var errors = new List<(string, string)>();
        var name = command.AcceptedBy?.Trim();
        if (string.IsNullOrEmpty(name)) errors.Add(("acceptedBy", "required"));
        else if (name.Length > DentalEstimateAcceptance.MaxName) errors.Add(("acceptedBy", "too_long"));
        if (command.Method is not { } method || !Enum.IsDefined(method)) errors.Add(("method", "invalid"));
        var items = await db.DentalPlanItems.AsNoTracking().Where(i => i.PatientId == patientId).ToListAsync(cancellationToken);
        if (!DentalRules.Estimated(items).Any()) errors.Add(("plan", "empty"));
        if (errors.Count > 0) throw new DentalValidationException(errors);
        var row = new DentalEstimateAcceptance
        {
            Id = Guid.CreateVersion7(), TenantId = TenantId, PatientId = patientId, FacilityId = facilityId, AcceptedByName = name!, Method = command.Method!.Value,
            TotalMinor = DentalRules.EstimateTotal(items), Currency = items[0].Currency, ItemsKey = DentalRules.EstimateKey(items), RecordedBy = Me, AcceptedAt = clock.GetUtcNow(),
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DentalEstimateAcceptances.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(AcceptAction, patientId, row.Id), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(row.Id, row.AcceptedByName, row.Method, row.TotalMinor, row.Currency, row.AcceptedAt, row.RecordedBy);
    }

    public async Task<DentalPdf> PdfAsync(Guid patientId, Guid facilityId, CancellationToken cancellationToken = default)
    {
        await clinical.RequireAsync(patientId, facilityId, Capability.Patient360Chart, [Access.Full, Access.Sign], cancellationToken);
        var chart = await ReadAsync(patientId, cancellationToken);
        var patient = (await patients.SummariesAsync([patientId], cancellationToken)).GetValueOrDefault(patientId) ?? throw new InvalidOperationException("The patient was not found.");
        var facility = await facilities.FindAsync(facilityId, cancellationToken) ?? throw new InvalidOperationException("Session facility not in this tenant.");
        var names = await staff.DisplayNamesAsync([.. chart.History.Select(f => f.RecordedBy).Append(Me).Distinct()], cancellationToken);
        var content = DentalPdfDocument.Render(patient, facility, chart, names, await facilities.PrintImagesAsync(Me, facilityId, cancellationToken),
            await facilities.PrescriberAsync(Me, facilityId, cancellationToken), names.GetValueOrDefault(Me) ?? "", clock.GetUtcNow());
        await audit.WriteAsync(Event(PdfAction, patientId), cancellationToken);
        return new DentalPdf(content);
    }

    public async Task<IReadOnlyList<DentalCharge>> DoneInVisitAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        await db.DentalPlanItems.AsNoTracking().Where(i => i.DoneAppointmentId == appointmentId && i.Status == DentalPlanStatus.Done)
            .OrderBy(i => i.DoneAt).ThenBy(i => i.Id).Select(i => new DentalCharge(i.Id, i.ServiceId)).ToListAsync(cancellationToken);

    static ToothFindingView View(DentalFinding f) => new(f.Id, f.Tooth, f.Condition, f.Surfaces, f.Note, f.EncounterId, f.RecordedBy, f.RecordedAt);

    static DentalPlanItemView View(DentalPlanItem i) =>
        new(i.Id, i.ServiceId, i.ServiceName, i.Tooth, i.Surfaces, i.Phase, i.EstimateMinor, i.Currency, i.Status, i.Reason, i.CreatedAt, i.ChangedAt, i.DoneAt, i.Version);

    AuditEvent Event(string action, Guid patientId, Guid? resource = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = resource is null ? "patient" : "dental_plan",
        ResourceId = resource ?? patientId, PatientId = patientId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(),
    };
}
