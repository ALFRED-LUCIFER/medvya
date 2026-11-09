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
        var me = Me;
        var mine = await db.DoctorNoteTemplates.AsNoTracking().Where(x => x.UserId == me).Select(x => x.TemplateKeys).SingleOrDefaultAsync(cancellationToken);
        return new([.. NoteTemplates.Keys.Select(k => NoteTemplates.Latest(k)!)], mine is { Length: > 0 } ? [.. NoteTemplates.Keys.Where(mine.Contains)] : [NoteTemplates.Default]);
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
