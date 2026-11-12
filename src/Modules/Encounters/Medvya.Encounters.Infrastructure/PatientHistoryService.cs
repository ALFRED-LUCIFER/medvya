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
/// History sections (MDV-P2-032): clinical access first, then the new version and its audit event in one transaction. A save never updates
/// or deletes a row, so every earlier text stays with its author and time.
/// </summary>
public sealed class PatientHistoryService(EncountersDbContext db, IClinicalAccess clinical, ICurrentTenant tenant, ICurrentUser user, IAuditLog audit, TimeProvider clock, IStaffUsers staff)
    : IPatientHistory
{
    public const string SaveAction = "patient.history.save";
    /// <summary>Doctors and nurses: the roles that enter vitals and allergies write history too.</summary>
    static readonly Access[] Writers = [Access.Full];

    public async Task<IReadOnlyList<HistoryEntryView>> SaveAsync(Guid patientId, Guid facilityId, HistorySection section, SaveHistory command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await clinical.RequireAsync(patientId, facilityId, Capability.VitalsAllergiesEntry, Writers, cancellationToken);
        var text = command.Text?.Trim() ?? "";
        List<(string, string)> errors = [];
        if (!Enum.IsDefined(section)) errors.Add(("section", "invalid"));
        if (text.Length == 0) errors.Add(("text", "required"));
        else if (text.Length > HistoryRules.MaxText) errors.Add(("text", "too_long"));
        else if (section == HistorySection.BloodGroup && !HistoryRules.BloodGroups.Contains(text)) errors.Add(("text", "invalid"));
        if (errors.Count > 0) throw new HistoryValidationException(errors);

        var (now, me) = (clock.GetUtcNow(), user.UserId ?? throw new InvalidOperationException("No user."));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.HistoryEntries.Add(new() { Id = Guid.CreateVersion7(), TenantId = tenantId, PatientId = patientId, Section = section, Text = text, RecordedBy = me, RecordedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorUserId = me, Action = SaveAction, ResourceType = "patient", ResourceId = patientId, PatientId = patientId,
            Purpose = AuditPurpose.Treatment, Outcome = AuditOutcome.Allowed, OccurredAt = now,
        }, tx.GetDbTransaction(), cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await ListAsync(db, staff, patientId, cancellationToken);
    }

    /// <summary>Every version of every section, newest first, with the author's name.</summary>
    internal static async Task<IReadOnlyList<HistoryEntryView>> ListAsync(EncountersDbContext db, IStaffUsers staff, Guid patientId, CancellationToken cancellationToken)
    {
        var rows = await db.HistoryEntries.AsNoTracking().Where(h => h.PatientId == patientId).OrderByDescending(h => h.RecordedAt).ThenByDescending(h => h.Id).ToListAsync(cancellationToken);
        var names = await staff.DisplayNamesAsync([.. rows.Select(h => h.RecordedBy).Distinct()], cancellationToken);
        return [.. rows.Select(h => new HistoryEntryView(h.Section, h.Text, names.GetValueOrDefault(h.RecordedBy), h.RecordedAt))];
    }
}
