using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Identity.Application;
using Medvya.Patients.Application;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// Investigation orders (MDV-P2-018). Clinical access first (Prescriptions and orders at Sign, care relationship, the encounter's facility),
/// then the work in one transaction with its audit event (purpose treatment, with the patient). Orders are composed and placed only while the
/// consultation is open. Placing is a call of its own: it is never a side effect of anything else. State moves are conditional updates, so two
/// simultaneous clicks cannot place or switch the same order twice.
/// </summary>
public sealed class OrderService(
    PrescribingDbContext db, IEncounterDirectory encounters, IClinicalAccess clinical, IPatientDirectory patients, IFacilityDirectory facilities,
    IStaffUsers staffUsers, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, PaperIssuer papers) : IOrders
{
    public const string ReadAction = "order.read", AddAction = "order.add", AddSetAction = "order.set.add", IncludeAction = "order.include",
        PlaceAction = "order.place", SlipAction = "order.slip", SlipPdfAction = "order.slip.pdf", Resource = "order";
    public const int MaxName = 200, MaxNote = 200, MaxDestination = 100;
    static readonly Access[] Doctors = [Access.Sign];

    public async Task<IReadOnlyList<OrderView>?> ListAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ReadAction, encounter, encounterId), cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>?> AddAsync(Guid encounterId, AddOrder command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        var name = Text(command.Name, "name", MaxName, true, errors);
        var note = Text(command.Note, "note", MaxNote, false, errors);
        if (command.Kind is not { } kind || !Enum.IsDefined(kind)) errors.Add(("kind", "required"));
        if (errors.Count > 0) throw new OrdersValidationException(errors);

        var id = Guid.CreateVersion7();
        await SaveAsync(encounter, AddAction, id, "count:1", async () =>
        {
            if (await Listed(encounterId, [name!]).AnyAsync(cancellationToken)) throw new OrdersConflictException("already_listed");
            db.Orders.Add(NewOrder(encounter, id, command.Kind!.Value, name!, note));
        }, cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>?> AddSetAsync(Guid encounterId, AddOrderSetOrders command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        if (command.OrderSetId is not { } setId) throw new OrdersValidationException([("orderSetId", "required")]);
        if (await db.OrderSets.AsNoTracking().SingleOrDefaultAsync(s => s.Id == setId, cancellationToken) is not { } set) return null;

        // A test already on the consultation (as a draft or placed) is not added twice; the rest are.
        var listed = await Listed(encounterId, set.Items).Select(o => o.Name).ToListAsync(cancellationToken);
        var fresh = set.Items.Where(i => !listed.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        if (fresh.Count == 0) throw new OrdersConflictException("already_listed");
        await SaveAsync(encounter, AddSetAction, setId, $"count:{fresh.Count}", () =>
        {
            db.Orders.AddRange(fresh.Select(i => NewOrder(encounter, Guid.CreateVersion7(), OrderKind.Lab, i, null)));
            return Task.CompletedTask;
        }, cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>?> IncludeAsync(Guid orderId, IncludeOrder command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken) is not { } order) return null;
        if (await RequireAsync(order.EncounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        if (command.Included is not { } included) throw new OrdersValidationException([("included", "required")]);

        var (from, to) = included ? (OrderState.NotIncluded, OrderState.Draft) : (OrderState.Draft, OrderState.NotIncluded);
        await SaveAsync(encounter, IncludeAction, orderId, $"state:{to}", async () =>
        {
            if (await db.Orders.Where(o => o.Id == orderId && o.State == from).ExecuteUpdateAsync(s => s.SetProperty(o => o.State, to), cancellationToken) == 0)
            {
                // Already where the caller wants it is fine; an ordered test is not a draft any more.
                if (!await db.Orders.AnyAsync(o => o.Id == orderId && o.State == to, cancellationToken)) throw new OrdersConflictException("not_draft");
            }
        }, cancellationToken);
        return await ViewsAsync(order.EncounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderView>?> PlaceAsync(Guid encounterId, PlaceOrders command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        var destination = Text(command.Destination, "destination", MaxDestination, false, errors);
        if (errors.Count > 0) throw new OrdersValidationException(errors);

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        await SaveAsync(encounter, PlaceAction, encounterId, null, async () =>
        {
            var placed = await db.Orders.Where(o => o.EncounterId == encounterId && o.State == OrderState.Draft)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, OrderState.Ordered).SetProperty(o => o.Destination, destination)
                    .SetProperty(o => o.OrderedBy, me).SetProperty(o => o.OrderedAt, now), cancellationToken);
            if (placed == 0) throw new OrdersConflictException("nothing_to_place");
        }, cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<OrderSlip?> SlipAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter || await BuildSlipAsync(encounter, cancellationToken) is not { } built) return null;
        await audit.WriteAsync(Event(SlipAction, encounter, encounterId), cancellationToken);
        return built.Slip;
    }

    public async Task<PaperPdf?> SlipPdfAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter || await BuildSlipAsync(encounter, cancellationToken) is not { } built) return null;
        var slip = built.Slip;
        return await papers.IssueAsync(PaperKind.InvestigationSlip, encounter.FacilityId, encounter.Id, encounter.LeadClinicianId, slip.Orders.Count, slip,
            paper => PaperPdfDocument.Slip(slip, built.TimeZone, paper), Event(SlipPdfAction, encounter, encounterId, $"tests:{slip.Orders.Count}"), cancellationToken);
    }

    /// <summary>The slip of the placed orders and the facility's time zone; null when nothing is placed.</summary>
    async Task<(OrderSlip Slip, string TimeZone)?> BuildSlipAsync(EncounterView encounter, CancellationToken cancellationToken)
    {
        var placed = await db.Orders.AsNoTracking().Where(o => o.EncounterId == encounter.Id && o.State == OrderState.Ordered).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(cancellationToken);
        if (placed.Count == 0) return null;
        var patient = (await patients.SummariesAsync([encounter.PatientId], cancellationToken)).GetValueOrDefault(encounter.PatientId) ?? throw new InvalidOperationException("Patient not in this tenant.");
        var facility = await facilities.FindAsync(encounter.FacilityId, cancellationToken) ?? throw new InvalidOperationException("Facility not in this tenant.");
        var doctor = (await staffUsers.DisplayNamesAsync([encounter.LeadClinicianId], cancellationToken)).GetValueOrDefault(encounter.LeadClinicianId);
        var destinations = placed.Select(o => o.Destination).Distinct().ToList();
        return (new OrderSlip(new SlipPatient(patient.FullName, patient.Mrn, patient.DateOfBirth, patient.SexAtBirth.ToString()), facility.Name, doctor,
            placed.Max(o => o.OrderedAt)!.Value, destinations.Count == 1 ? destinations[0] : null, placed.Select(View).ToList()), facility.TimeZone);
    }

    /// <summary>The encounter, once the caller may order for its patient at its facility. Null: no such encounter in this tenant.</summary>
    async Task<EncounterView?> RequireAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        if (await encounters.FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        return encounter;
    }

    static void Open(EncounterView encounter)
    {
        if (encounter.Status != EncounterStatus.Active) throw new OrdersConflictException("encounter_ended");
    }

    IQueryable<ClinicalOrder> Listed(Guid encounterId, IReadOnlyCollection<string> names)
    {
        // Case-insensitive match without ToLower: ILike on the escaped name, OR-ed.
        var patterns = names.Select(n => n.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)).ToArray();
        return db.Orders.AsNoTracking().Where(o => o.EncounterId == encounterId && o.State != OrderState.NotIncluded && patterns.Any(p => EF.Functions.ILike(o.Name, p)));
    }

    ClinicalOrder NewOrder(EncounterView encounter, Guid id, OrderKind kind, string name, string? note) => new()
    {
        Id = id, TenantId = TenantId, FacilityId = encounter.FacilityId, PatientId = encounter.PatientId, EncounterId = encounter.Id, Kind = kind, Name = name, Note = note,
        State = OrderState.Draft, CreatedBy = user.UserId ?? throw new InvalidOperationException("No user."), CreatedAt = clock.GetUtcNow(),
    };

    async Task SaveAsync(EncounterView encounter, string action, Guid resourceId, string? detail, Func<Task> change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, encounter, resourceId, detail), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    async Task<IReadOnlyList<OrderView>> ViewsAsync(Guid encounterId, CancellationToken cancellationToken) =>
        (await db.Orders.AsNoTracking().Where(o => o.EncounterId == encounterId).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(cancellationToken)).Select(View).ToList();

    static OrderView View(ClinicalOrder o) => new(o.Id, o.Kind, o.Name, o.Note, o.State, o.Destination, o.OrderedAt);

    static string? Text(string? value, string field, int max, bool required, List<(string, string)> errors)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (v is null && required) errors.Add((field, "required"));
        else if (v?.Length > max) errors.Add((field, "too_long"));
        return v;
    }

    AuditEvent Event(string action, EncounterView encounter, Guid resourceId, string? detail = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = TenantId, ActorUserId = user.UserId, Action = action, ResourceType = Resource, ResourceId = resourceId,
        PatientId = encounter.PatientId, FacilityId = encounter.FacilityId, Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = clock.GetUtcNow(), Detail = detail,
    };

    Guid TenantId => tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
}

/// <summary>Orders on the patient's timeline in every state (MDV-P2-018), through the timeline's own contract.</summary>
public sealed class OrderTimelineSource(PrescribingDbContext db) : ITimelineSource
{
    public async Task<IReadOnlyList<TimelineEntry>> EntriesAsync(Guid patientId, DateTimeOffset? upTo, int take, CancellationToken cancellationToken = default)
    {
        var rows = await db.Orders.AsNoTracking().Where(o => o.PatientId == patientId && (upTo == null || (o.OrderedAt ?? o.CreatedAt) <= upTo))
            .OrderByDescending(o => o.OrderedAt ?? o.CreatedAt).ThenByDescending(o => o.Id).Take(take).ToListAsync(cancellationToken);
        return rows.Select(o => new TimelineEntry(o.Id, TimelineKind.Order, o.OrderedAt ?? o.CreatedAt, $"Order{o.State}", o.Name, o.Note, o.OrderedBy ?? o.CreatedBy, ChartSource.ClinicianVerified)).ToList();
    }
}

/// <summary>The tests placed at these encounters, for the chart's Visits block (MDV-1088), through the Encounters contract. Drafts and switched-off orders are not tests of the visit.</summary>
public sealed class VisitTests(PrescribingDbContext db) : Medvya.Encounters.Application.IVisitTests
{
    public async Task<IReadOnlyList<Medvya.Encounters.Application.PlacedTest>> ForEncountersAsync(IReadOnlyCollection<Guid> encounterIds, CancellationToken cancellationToken = default) =>
        await db.Orders.AsNoTracking().Where(o => o.State == OrderState.Ordered && encounterIds.Contains(o.EncounterId))
            .OrderBy(o => o.Name).Select(o => new Medvya.Encounters.Application.PlacedTest(o.EncounterId, o.Name)).ToListAsync(cancellationToken);
}

/// <summary>The patient's placed orders for the "ordered but never resulted" care gap (MDV-P2-038), through the Encounters contract.</summary>
public sealed class PlacedOrders(PrescribingDbContext db) : IPlacedOrders
{
    public async Task<IReadOnlyList<PlacedOrderFact>> SinceAsync(Guid patientId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        await db.Orders.AsNoTracking().Where(o => o.PatientId == patientId && o.State == OrderState.Ordered && o.OrderedAt >= since)
            .Select(o => new PlacedOrderFact(o.Name, o.OrderedAt!.Value)).ToListAsync(cancellationToken);
}

/// <summary>A patient merge moves the surviving record's orders, prescription lines and referrals along and an undo moves them back (MDV-P1-017), in the merge's transaction.</summary>
public sealed class OrderMergeParticipant(ICurrentTenant tenant) : IPatientMergeParticipant
{
    public Task<bool> HasClinicalDataAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(false); // the patient's encounters already count

    public Task<int> VisitsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(0);

    public async Task MoveAsync(Guid fromPatientId, Guid toPatientId, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        await shared.Orders.Where(o => o.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PatientId, toPatientId).SetProperty(o => o.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.PrescriptionItems.Where(i => i.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PatientId, toPatientId).SetProperty(i => i.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.RxAlertOverrides.Where(o => o.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PatientId, toPatientId).SetProperty(o => o.MergedFromPatientId, fromPatientId), cancellationToken);
        await shared.Referrals.Where(r => r.PatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PatientId, toPatientId).SetProperty(r => r.MergedFromPatientId, fromPatientId), cancellationToken);
    }

    public async Task RestoreAsync(Guid fromPatientId, Guid toPatientId, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var shared = await SharedAsync(transaction, cancellationToken);
        await shared.Orders.Where(o => o.PatientId == toPatientId && o.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PatientId, fromPatientId).SetProperty(o => o.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.PrescriptionItems.Where(i => i.PatientId == toPatientId && i.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PatientId, fromPatientId).SetProperty(i => i.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.RxAlertOverrides.Where(o => o.PatientId == toPatientId && o.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PatientId, fromPatientId).SetProperty(o => o.MergedFromPatientId, (Guid?)null), cancellationToken);
        await shared.Referrals.Where(r => r.PatientId == toPatientId && r.MergedFromPatientId == fromPatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PatientId, fromPatientId).SetProperty(r => r.MergedFromPatientId, (Guid?)null), cancellationToken);
    }

    async Task<PrescribingDbContext> SharedAsync(System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var shared = new PrescribingDbContext(new DbContextOptionsBuilder<PrescribingDbContext>().UseNpgsql(transaction.Connection!).Options, tenant);
        await shared.Database.UseTransactionAsync(transaction, cancellationToken);
        return shared;
    }
}
