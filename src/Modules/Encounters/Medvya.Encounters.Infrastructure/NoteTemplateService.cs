using Medvya.BuildingBlocks.Domain;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Encounters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Encounters.Infrastructure;

/// <summary>Note templates (MDV-P2-025): the catalogue is code, the doctor's choice is one row per doctor. No patient data, so no patient audit.</summary>
public sealed class NoteTemplateService(EncountersDbContext db, ICurrentTenant tenant, ICurrentUser user) : INoteTemplates
{
    Guid Me => user.UserId ?? throw new InvalidOperationException("No user.");

    public async Task<NoteTemplateCatalog> CatalogAsync(CancellationToken cancellationToken = default)
    {
        return new([.. NoteTemplates.Keys.Select(k => NoteTemplates.Latest(k)!)], await WorkingListAsync(db, Me, cancellationToken));
    }

    /// <summary>The templates a doctor works with: their own choice, else the clinic default (MDV-P8-015), else General OPD.</summary>
    internal static async Task<IReadOnlyList<string>> WorkingListAsync(EncountersDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var mine = await db.DoctorNoteTemplates.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.TemplateKeys).SingleOrDefaultAsync(cancellationToken);
        return mine is { Length: > 0 } ? [.. NoteTemplates.Keys.Where(mine.Contains)] : await ClinicDefaultAsync(db, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ClinicDefaultAsync(CancellationToken cancellationToken = default) => ClinicDefaultAsync(db, cancellationToken);

    static async Task<IReadOnlyList<string>> ClinicDefaultAsync(EncountersDbContext db, CancellationToken cancellationToken)
    {
        var keys = await db.ClinicNoteTemplates.AsNoTracking().Select(x => x.TemplateKeys).SingleOrDefaultAsync(cancellationToken);
        return keys is { Length: > 0 } ? [.. NoteTemplates.Keys.Where(keys.Contains)] : [NoteTemplates.Default];
    }

    public async Task<IReadOnlyList<string>?> SetClinicDefaultAsync(ChooseNoteTemplates command, CancellationToken cancellationToken = default)
    {
        var keys = command?.Keys?.Distinct().ToList();
        if (keys is not { Count: > 0 } || keys.Exists(k => NoteTemplates.Latest(k) is null)) return null;
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant.");
        var row = await db.ClinicNoteTemplates.SingleOrDefaultAsync(cancellationToken);
        if (row is null) db.ClinicNoteTemplates.Add(new ClinicNoteTemplate { TenantId = tenantId, TemplateKeys = [.. keys] });
        else row.TemplateKeys = [.. keys];
        await db.SaveChangesAsync(cancellationToken);
        return [.. NoteTemplates.Keys.Where(keys.Contains)];
    }

    public async Task<IReadOnlyList<string>?> ChooseAsync(ChooseNoteTemplates command, CancellationToken cancellationToken = default)
    {
        var keys = command?.Keys?.Distinct().ToList();
        if (keys is not { Count: > 0 } || keys.Exists(k => NoteTemplates.Latest(k) is null)) return null;
        var (me, tenantId) = (Me, tenant.TenantId ?? throw new InvalidOperationException("No tenant."));
        var row = await db.DoctorNoteTemplates.SingleOrDefaultAsync(x => x.UserId == me, cancellationToken);
        if (row is null) db.DoctorNoteTemplates.Add(new DoctorNoteTemplate { Id = Guid.CreateVersion7(), TenantId = tenantId, UserId = me, TemplateKeys = [.. keys] });
        else row.TemplateKeys = [.. keys];
        await db.SaveChangesAsync(cancellationToken);
        return [.. NoteTemplates.Keys.Where(keys.Contains)];
    }
}
