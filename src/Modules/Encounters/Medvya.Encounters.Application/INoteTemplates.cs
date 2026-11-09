using Medvya.Encounters.Domain;

namespace Medvya.Encounters.Application;

/// <summary>The latest version of every template and the keys this doctor works with (General OPD when none were chosen).</summary>
public sealed record NoteTemplateCatalog(IReadOnlyList<NoteTemplate> Templates, IReadOnlyList<string> Mine);

public sealed record ChooseNoteTemplates(IReadOnlyList<string>? Keys);

public interface INoteTemplates
{
    Task<NoteTemplateCatalog> CatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the caller's choice. Null when a key is not a template.</summary>
    Task<IReadOnlyList<string>?> ChooseAsync(ChooseNoteTemplates command, CancellationToken cancellationToken = default);
}
