using System.Globalization;
using System.Text;

namespace Medvya.Patients.Application;

/// <summary>
/// "Name similarity" for the duplicate check (MDV-P1-016). Word by word, so order, middle names and initials matter little:
/// "Deepak Nair" ~ "Deepak K. Nair" and "Fatima Al Mansoori" ~ "Fatima Ahmed Al Mansouri", but "Meera Iyer" !~ "Meera Rao".
/// shortcut: thresholds are a first cut; tune them on pilot data (the ticket's confidence note), and add transliteration
/// (Arabic ↔ Latin, Indic scripts) when names are compared across scripts.
/// </summary>
public static class NameMatch
{
    /// <summary>Jaro-Winkler at or above this counts two words as the same (absorbs one-letter spelling differences).</summary>
    public const double WordThreshold = 0.88;

    /// <summary>Share of the shorter name's words that must match a word of the other name.</summary>
    public const double NameThreshold = 2.0 / 3;

    public static bool Similar(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        var (shorter, longer) = x.Count <= y.Count ? (x, y) : (y, x);
        if (shorter.Count == 0) return false;
        var matched = shorter.Count(w => longer.Exists(v => JaroWinkler(w, v) >= WordThreshold));
        return matched >= Math.Min(2, shorter.Count) && matched >= NameThreshold * shorter.Count;
    }

    /// <summary>Lower-case words of two or more letters, accents removed; initials and punctuation are dropped.</summary>
    internal static List<string> Words(string name)
    {
        var plain = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetter(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return [.. plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2)];
    }

    internal static double JaroWinkler(string s, string t)
    {
        if (s == t) return 1;
        var range = Math.Max(0, Math.Max(s.Length, t.Length) / 2 - 1);
        var sMatched = new bool[s.Length];
        var tMatched = new bool[t.Length];
        var matches = 0;
        for (var i = 0; i < s.Length; i++)
            for (var j = Math.Max(0, i - range); j < Math.Min(t.Length, i + range + 1); j++)
            {
                if (tMatched[j] || s[i] != t[j]) continue;
                sMatched[i] = tMatched[j] = true;
                matches++;
                break;
            }
        if (matches == 0) return 0;

        double transpositions = 0;
        for (int i = 0, k = 0; i < s.Length; i++)
        {
            if (!sMatched[i]) continue;
            while (!tMatched[k]) k++;
            if (s[i] != t[k]) transpositions++;
            k++;
        }
        var m = (double)matches;
        var jaro = (m / s.Length + m / t.Length + (m - transpositions / 2) / m) / 3;
        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(s.Length, t.Length)) && s[prefix] == t[prefix]) prefix++;
        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
