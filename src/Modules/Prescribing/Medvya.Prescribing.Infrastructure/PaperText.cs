using System.Text.Json;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>The server papers' wording, from the shared English catalogue (packages/i18n/messages/en.json, embedded): the server never invents copy.</summary>
static class PaperText
{
    static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(PaperText).Assembly.GetManifestResourceStream("Medvya.Prescribing.messages.en.json")
            ?? throw new InvalidOperationException("The English message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    public static string Get(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
}
