using System.Text.Json;
using System.Text.RegularExpressions;

namespace Host.Api.PublicSite;

/// <summary>
/// Copy for the public site from the shared message catalogues (packages/i18n/messages), with the same rules as the
/// front end's translate(): missing translations fall back to English and {name} placeholders are filled in.
/// </summary>
public sealed partial class SiteText
{
    readonly Dictionary<string, Dictionary<string, string>> catalogs = PublicSite.Languages.ToDictionary(l => l, Load);

    public string Get(string lang, string key, IReadOnlyDictionary<string, string>? vars = null)
    {
        var text = (catalogs.TryGetValue(lang, out var cat) && cat.TryGetValue(key, out var t) ? t : null)
            ?? catalogs["en"].GetValueOrDefault(key)
            ?? throw new KeyNotFoundException($"Message key '{key}' is not in packages/i18n/messages/en.json.");
        return vars is null ? text : Placeholder().Replace(text, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    static Dictionary<string, string> Load(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(PublicSite.Resource($"messages.{lang}.json"))!
            .Where(kv => !kv.Key.StartsWith('_')) // reviewer notes, not copy
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
