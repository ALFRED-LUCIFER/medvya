using Medvya.Audit.Application;
using Medvya.Audit.Domain;
using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Prescribing.Infrastructure.Persistence;
using Medvya.Tenancy.Application;
using Medvya.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The draft prescription composer (MDV-P2-015). Clinical access first (Prescriptions and orders at Sign, care relationship, the encounter's
/// facility), then the work in one transaction with its audit event (purpose treatment, with the patient; codes only, never a medicine). Lines are
/// changed only while the consultation is open. Discontinue lines are not stored: they are the reconciliation's current stop decisions, so a
/// changed decision changes the line. Nothing here authorises: that is its own step (MDV-P2-017).
/// </summary>
public sealed class PrescriptionService(
    PrescribingDbContext db, IEncounterDirectory encounters, IReconciliationStops stops, IPrescribingChart chart, IClinicalAccess clinical, IStaffAccess staff,
    ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock) : IPrescriptions
{
    public const string ReadAction = "rx.read", AddAction = "rx.add", UpdateAction = "rx.update", IncludeAction = "rx.include",
        CheckAction = "rx.check", OverrideAction = "rx.override", Resource = "prescription";
    public const int MaxMedicine = 200, MaxStrength = 50, MaxReason = 200;
    static readonly Access[] Doctors = [Access.Sign];

    public async Task<IReadOnlyList<RxLineView>?> ListAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await audit.WriteAsync(Event(ReadAction, encounter, encounterId), cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxLineView>?> AddAsync(Guid encounterId, AddRxLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        string medicine;
        string? strength, form;
        if (command.FormularyItemId is { } itemId)
        {
            var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
            if (await db.FormularyItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.Country == country && i.Active, cancellationToken) is not { } item)
                throw new PrescriptionValidationException([("formularyItemId", "unknown")]);
            (medicine, strength, form) = (item.Generic, item.Strength, item.Form);
        }
        else
        {
            // Free text: the doctor's words, flagged for review because no formulary entry (and so no interaction data) stands behind it.
            medicine = Text(command.Medicine, "medicine", MaxMedicine, true, errors) ?? "";
            strength = Text(command.Strength, "strength", MaxStrength, false, errors);
            form = Text(command.Form, "form", 40, false, errors);
            if (form is not null && !FormularyRules.Forms.Contains(form)) errors.Add(("form", "unknown"));
        }
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);

        var (id, now) = (Guid.CreateVersion7(), clock.GetUtcNow());
        await SaveAsync(encounter, AddAction, id, command.FormularyItemId is null ? "free_text" : "formulary", () => db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = id, TenantId = TenantId, FacilityId = encounter.FacilityId, PatientId = encounter.PatientId, EncounterId = encounter.Id, FormularyItemId = command.FormularyItemId,
            Medicine = medicine, Strength = strength, Form = form, CreatedBy = Me, CreatedAt = now, UpdatedAt = now,
        }), cancellationToken);
        return await ViewsAsync(encounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxLineView>?> UpdateAsync(Guid itemId, UpdateRxLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.PrescriptionItems.SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken) is not { } item) return null;
        if (await RequireAsync(item.EncounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var sig = new SigParts(Blank(command.Dose), Blank(command.DoseUnit), Blank(command.Route), Blank(command.Frequency), Blank(command.Timing),
            command.DurationDays, command.Quantity, Blank(command.Instructions));
        if (Sig.Validate(sig) is { Count: > 0 } errors) throw new PrescriptionValidationException(errors);

        await SaveAsync(encounter, UpdateAction, itemId, null, () =>
        {
            (item.Dose, item.DoseUnit, item.Route, item.Frequency, item.Timing) = (sig.Dose, sig.DoseUnit, sig.Route, sig.Frequency, sig.Timing);
            (item.DurationDays, item.Quantity, item.Instructions, item.UpdatedAt) = (sig.DurationDays, sig.Quantity, sig.Instructions, clock.GetUtcNow());
        }, cancellationToken);
        return await ViewsAsync(item.EncounterId, cancellationToken);
    }

    public async Task<IReadOnlyList<RxLineView>?> IncludeAsync(Guid itemId, IncludeRxLine command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await db.PrescriptionItems.SingleOrDefaultAsync(i => i.Id == itemId, cancellationToken) is not { } item) return null;
        if (await RequireAsync(item.EncounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        if (command.Included is not { } included) throw new PrescriptionValidationException([("included", "required")]);
        await SaveAsync(encounter, IncludeAction, itemId, $"included:{included}", () => (item.Included, item.UpdatedAt) = (included, clock.GetUtcNow()), cancellationToken);
        return await ViewsAsync(item.EncounterId, cancellationToken);
    }

    public async Task<RxCheckView?> CheckAsync(Guid encounterId, CancellationToken cancellationToken = default)
    {
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        var view = await ChecksAsync(encounter, cancellationToken);
        // The result is recorded with the rule set that gave it: version and counts only, never a medicine or an allergy.
        await audit.WriteAsync(Event(CheckAction, encounter, encounterId,
            $"rules:{view.RuleSetVersion};severe:{view.Alerts.Count(a => a.Severity == RxSeverity.Severe)};moderate:{view.Alerts.Count(a => a.Severity == RxSeverity.Moderate)};unresolved:{view.Unresolved}"), cancellationToken);
        return view;
    }

    public async Task<RxCheckView?> OverrideAsync(Guid encounterId, OverrideRxAlert command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await RequireAsync(encounterId, cancellationToken) is not { } encounter) return null;
        Open(encounter);
        var errors = new List<(string, string)>();
        var reason = Text(command.Reason, "reason", MaxReason, true, errors);
        // Only a severe alert of the check as it stands now: an old key (the dose changed, the line went) needs the new finding's reason.
        var alert = (await ChecksAsync(encounter, cancellationToken)).Alerts.FirstOrDefault(a => a.Key == command.Key && a.Severity == RxSeverity.Severe);
        if (alert is null) errors.Add(("key", "unknown"));
        if (errors.Count > 0) throw new PrescriptionValidationException(errors);

        var id = Guid.CreateVersion7();
        await SaveAsync(encounter, OverrideAction, id, $"kind:{alert!.Kind};rules:{SafetyRules.Version}", () => db.RxAlertOverrides.Add(new RxAlertOverride
        {
            Id = id, TenantId = TenantId, FacilityId = encounter.FacilityId, PatientId = encounter.PatientId, EncounterId = encounter.Id, AlertKey = alert.Key,
            Kind = alert.Kind, Reason = reason!, RuleSetVersion = SafetyRules.Version, OverriddenBy = Me, OverriddenAt = clock.GetUtcNow(),
        }), cancellationToken);
        return await ChecksAsync(encounter, cancellationToken);
    }

    /// <summary>The checks over the included prescribe lines and the chart's active medicines (less those stopped in this visit), with the current overrides
    /// (made under this rule set version: a new version asks again).</summary>
    async Task<RxCheckView> ChecksAsync(EncounterView encounter, CancellationToken cancellationToken)
    {
        var items = await db.PrescriptionItems.AsNoTracking().Where(i => i.EncounterId == encounter.Id && i.Included).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var formularyIds = items.Where(i => i.FormularyItemId is not null).Select(i => i.FormularyItemId!.Value).ToList();
        var atcOf = await db.FormularyItems.AsNoTracking().Where(f => formularyIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Atc, cancellationToken);
        var facts = await chart.FactsAsync(encounter.PatientId, cancellationToken);
        // The generic names are read only when there are chart medicines to match (a clinic's formulary can hold thousands of rows).
        Dictionary<string, string?> generics = new(StringComparer.OrdinalIgnoreCase);
        if (facts.ActiveMedicines.Count > 0)
        {
            var country = await staff.TenantCountryAsync(cancellationToken) ?? throw new InvalidOperationException("No tenant country.");
            generics = (await db.FormularyItems.AsNoTracking().Where(f => f.Country == country && f.Active && f.Atc != null).Select(f => new { f.Generic, f.Atc }).ToListAsync(cancellationToken))
                .GroupBy(f => f.Generic, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(f => f.Atc!).Distinct().Count() == 1 ? g.First().Atc : null, StringComparer.OrdinalIgnoreCase);
        }
        var stopped = (await stops.StopsAsync(encounter.Id, cancellationToken)).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lines = items.Select(i => new CheckedMedicine(i.Id, i.Medicine, i.FormularyItemId is { } f ? atcOf.GetValueOrDefault(f) : null, i.Strength,
            new SigParts(i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions), false, i.FormularyItemId is null)).ToList();
        // A chart medicine is checked only when its name names one formulary generic with one ATC code; otherwise it is reported as not checked.
        var onChart = facts.ActiveMedicines.Where(m => !stopped.Contains(m.Name))
            .Select(m => new CheckedMedicine(m.Id, m.Name, SafetyRules.MatchGeneric(m.Name, generics.Keys) is { } g ? generics[g] : null, null, null, true, false)).ToList();
        var state = facts.AllergyState switch
        {
            AllergyState.Listed => ChartAllergies.Listed,
            AllergyState.NoKnownAllergies => ChartAllergies.NoKnownAllergies,
            _ => ChartAllergies.NotRecorded,
        };
        var result = SafetyRules.Evaluate(lines, onChart, state, [.. facts.Allergies.Select(a => new CheckedAllergy(a.Id, a.Substance, a.Status.ToString()))]);

        var keys = result.Alerts.Select(a => a.Key).ToList();
        var overrides = (await db.RxAlertOverrides.AsNoTracking().Where(o => o.EncounterId == encounter.Id && o.RuleSetVersion == SafetyRules.Version && keys.Contains(o.AlertKey)).ToListAsync(cancellationToken))
            .GroupBy(o => o.AlertKey).Select(g => g.OrderByDescending(o => o.OverriddenAt).ThenByDescending(o => o.Id).First())
            .Select(o => new RxOverrideView(o.AlertKey, o.Reason, o.RuleSetVersion, o.OverriddenBy, o.OverriddenAt)).ToList();
        var unresolved = result.Alerts.Count(a => a.Severity == RxSeverity.Severe && overrides.All(o => o.Key != a.Key));
        return new(SafetyRules.Source, SafetyRules.Version, state, result.Alerts, result.NotChecked, overrides, unresolved, clock.GetUtcNow());
    }

    /// <summary>The encounter, once the caller may prescribe for its patient at its facility. Null: no such encounter in this tenant.</summary>
    async Task<EncounterView?> RequireAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        if (await encounters.FindAsync(encounterId, cancellationToken) is not { } encounter) return null;
        await clinical.RequireAsync(encounter.PatientId, encounter.FacilityId, Capability.PrescriptionsOrders, Doctors, cancellationToken);
        return encounter;
    }

    static void Open(EncounterView encounter)
    {
        if (encounter.Status != EncounterStatus.Active) throw new PrescriptionConflictException("encounter_ended");
    }

    async Task SaveAsync(EncounterView encounter, string action, Guid resourceId, string? detail, Action change, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        change();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(Event(action, encounter, resourceId, detail), tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    async Task<IReadOnlyList<RxLineView>> ViewsAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        var items = await db.PrescriptionItems.AsNoTracking().Where(i => i.EncounterId == encounterId).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var stopped = await stops.StopsAsync(encounterId, cancellationToken);
        return
        [
            .. items.Select(i =>
            {
                var sig = new SigParts(i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions);
                return new RxLineView(i.Id, RxLineKind.Prescribe, i.CreatedBy == Me ? RxSource.AddedByYou : RxSource.AddedByColleague, i.FormularyItemId, i.Medicine, i.Strength, i.Form,
                    i.FormularyItemId is null, i.Included, i.Dose, i.DoseUnit, i.Route, i.Frequency, i.Timing, i.DurationDays, i.Quantity, i.Instructions, null,
                    Sig.Languages.ToDictionary(l => l, l => Sig.Render(sig, l)));
            }),
            .. stopped.Select(s => new RxLineView(null, RxLineKind.Discontinue, RxSource.ChartReconciliation, null, s.Name, null, null, false, true,
                null, null, null, null, null, null, null, null, s.Reason, Sig.Languages.ToDictionary(l => l, Sig.Stop))),
        ];
    }

    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static string? Text(string? value, string field, int max, bool required, List<(string, string)> errors)
    {
        var v = Blank(value);
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
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");
}
