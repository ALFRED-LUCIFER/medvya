using System.Text.Json;
using System.Text.Json.Nodes;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Encounters.Domain;

/// <summary>
/// The stored text of a structured section (MDV-P2-042), kept in <see cref="ConsultNote.Sections"/> like any other section so the signed hash and
/// the ledger need nothing new. Format: a JSON object from item id to <c>{"answer": "...", "note": "..."}</c>, items in the template's order,
/// <c>note</c> only when it has text. A review of systems answers <see cref="Yes"/> or <see cref="No"/>; an examination <see cref="Normal"/>,
/// <see cref="Abnormal"/> (a note is required) or <see cref="NotExamined"/>. An item not answered is absent: blank stays blank, nothing defaults to normal.
/// </summary>
public static class StructuredAnswers
{
    public const string Yes = "yes", No = "no", Normal = "normal", Abnormal = "abnormal", NotExamined = "notExamined";

    public sealed record Answer(
        [property: Classification(DataClass.Internal)] string Item, [property: Classification(DataClass.SensitiveHealth)] string Value,
        [property: Classification(DataClass.SensitiveHealth)] string? Note);

    static string[] Allowed(SectionKind kind) => kind == SectionKind.ReviewOfSystems ? [Yes, No] : [Normal, Abnormal, NotExamined];

    /// <summary>
    /// Checks the editor's text and returns the canonical text to store (null when no item is answered), or the field/code errors.
    /// Field names are <c>sections.&lt;id&gt;</c> or <c>sections.&lt;id&gt;.&lt;item&gt;</c>, codes only, never the text.
    /// </summary>
    public static (string? Canonical, IReadOnlyList<(string Field, string Code)> Errors) Normalise(NoteSection section, string text)
    {
        ArgumentNullException.ThrowIfNull(section);
        var field = $"sections.{section.Id}";
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }
        if (root is null) return (null, [(field, "invalid")]);

        var items = section.Items ?? [];
        var errors = new List<(string, string)>();
        var answers = new Dictionary<string, Answer>();
        foreach (var (item, node) in root)
        {
            if (!items.Contains(item)) { errors.Add(($"{field}.{item}", "unknown_item")); continue; }
            string? value = null, note = null;
            try
            {
                value = node?["answer"]?.GetValue<string>();
                note = node?["note"]?.GetValue<string>()?.Trim();
            }
            catch (Exception e) when (e is InvalidOperationException or FormatException) { /* a non-string answer or note is invalid, below */ }
            if (value is null || !Allowed(section.Kind).Contains(value)) { errors.Add(($"{field}.{item}", "invalid")); continue; }
            // SAFETY: an abnormal finding without its note is refused; remove this and StructuredNoteTests fails.
            if (value == Abnormal && string.IsNullOrEmpty(note)) { errors.Add(($"{field}.{item}", "note_required")); continue; }
            answers[item] = new(item, value, string.IsNullOrEmpty(note) ? null : note);
        }
        if (errors.Count > 0) return (null, errors);
        if (answers.Count == 0) return (null, []);
        var canonical = new JsonObject();
        foreach (var item in items.Where(answers.ContainsKey))
        {
            var a = answers[item];
            canonical[item] = a.Note is null ? new JsonObject { ["answer"] = a.Value } : new JsonObject { ["answer"] = a.Value, ["note"] = a.Note };
        }
        return (canonical.ToJsonString(), []);
    }

    /// <summary>The answers of stored (canonical) text, in the template's order: what the signed note and its PDF print in words.</summary>
    public static IReadOnlyList<Answer> Read(NoteSection section, string canonical)
    {
        ArgumentNullException.ThrowIfNull(section);
        var root = JsonNode.Parse(canonical)!.AsObject();
        return [.. (section.Items ?? []).Where(root.ContainsKey).Select(i => new Answer(i, root[i]!["answer"]!.GetValue<string>(), root[i]!["note"]?.GetValue<string>()))];
    }
}
