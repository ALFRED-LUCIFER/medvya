using System.Globalization;
using System.Text;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The tooth chart drawn on the dental PDF, as on screen (ToothChart, DN-02): FDI teeth facing the patient, each drawn as its own type with its five
/// surfaces inside, caries hatched and fillings solid on the recorded surfaces, a crown dotted, a missing or implant tooth faded, the condition's symbol on
/// top, and the code printed under it with its surfaces ("C·DO"), so print never relies on colour. An unrecorded tooth is dashed with a dash. Primary rows
/// are printed only when a primary tooth has a finding.
/// </summary>
public static class DentalChartPdf
{
    sealed record Look(string Text, string Bg, string Border);
    static readonly Dictionary<string, Look> Tones = new()
    {
        ["danger"] = new("#8a1626", "#fdecee", "#c83a4b"),
        ["warning"] = new("#7a4710", "#fdf5e7", "#b7791f"),
        ["success"] = new("#07774f", "#e6f4ef", "#087f5b"),
        ["info"] = new("#1d4ed8", "#eff4ff", "#2563eb"),
        ["brand"] = new("#0f766e", "#e7f6f4", "#0e9384"),
        ["neutral"] = new("#334e68", "#f1f5f8", "#76899c"),
    };

    // Tone, fill and symbol of each condition: the same table as the web chart (conditionTone and conditionDrawing in features/dental/domain/dental.ts).
    static readonly Dictionary<ToothCondition, (string Tone, string? Fill, string? Symbol)> Drawing = new()
    {
        [ToothCondition.Sound] = ("success", null, "check"),
        [ToothCondition.Caries] = ("danger", "hatch", null),
        [ToothCondition.Filling] = ("info", "solid", null),
        [ToothCondition.Crown] = ("warning", "dots", null),
        [ToothCondition.RootCanalTreated] = ("info", null, "line"),
        [ToothCondition.Missing] = ("neutral", "faded", "cross"),
        [ToothCondition.Implant] = ("brand", "faded", "screw"),
        [ToothCondition.Bridge] = ("warning", null, "bar"),
        [ToothCondition.Mobile] = ("warning", null, "arrows"),
        [ToothCondition.Fractured] = ("danger", null, "zigzag"),
    };

    static readonly Dictionary<string, string> Symbols = new()
    {
        ["line"] = "M18 1 V35",
        ["cross"] = "M5 5 L31 31 M31 5 L5 31",
        ["screw"] = "M18 4 V32 M12 9 H24 M12 15 H24 M12 21 H24 M14 27 H22",
        ["bar"] = "M0 18 H36",
        ["arrows"] = "M5 18 H31 M9 13 L4 18 L9 23 M27 13 L32 18 L27 23",
        ["zigzag"] = "M11 3 L21 13 L14 19 L25 33",
        ["check"] = "M10 19 L16 25 L27 11", // healthy: the web chart prints ✓, which the PDF fonts do not have, so print draws it
    };

    // Primary 1 to 5 drawn as the permanent types 1, 2, 3 and, for the primary molars, 6 and 7.
    static readonly int[] PrimaryType = [0, 1, 2, 3, 6, 7];

    static string[] Quadrant(int q, int teeth, bool reverse)
    {
        var list = Enumerable.Range(1, teeth).Select(i => $"{q}{i}");
        return [.. reverse ? list.Reverse() : list];
    }

    /// <summary>The rows as drawn facing the patient, each [patient's right half, patient's left half]; the same order as toothRows in the web chart.</summary>
    internal static IReadOnlyList<(string[] Right, string[] Left)> Rows(bool mixed) => mixed
        ? [(Quadrant(1, 8, true), Quadrant(2, 8, false)), (Quadrant(5, 5, true), Quadrant(6, 5, false)), (Quadrant(8, 5, true), Quadrant(7, 5, false)), (Quadrant(4, 8, true), Quadrant(3, 8, false))]
        : [(Quadrant(1, 8, true), Quadrant(2, 8, false)), (Quadrant(4, 8, true), Quadrant(3, 8, false))];

    internal static Action<IContainer> Part(IReadOnlyList<ToothFindingView> teeth, Func<string, string> text) => e =>
    {
        var byTooth = teeth.ToDictionary(t => t.Tooth);
        var rows = Rows(teeth.Any(t => t.Tooth[0] is >= '5' and <= '8'));
        e.PaddingBottom(4).Column(c =>
        {
            c.Spacing(2);
            c.Item().Row(r =>
            {
                r.RelativeItem().Text(text("toothChart.patientRight")).FontSize(6.5f).SemiBold().FontColor("#506579");
                r.RelativeItem().AlignRight().Text(text("toothChart.patientLeft")).FontSize(6.5f).SemiBold().FontColor("#506579");
            });
            for (var i = 0; i < rows.Count; i++)
            {
                if (i == rows.Count / 2) c.Item().LineHorizontal(0.6f).LineColor("#cbd6e0");
                var (right, left) = rows[i];
                c.Item().Row(r =>
                {
                    r.Spacing(1.2f);
                    foreach (var n in right) r.RelativeItem().Element(x => Tooth(x, n, byTooth.GetValueOrDefault(n), text));
                    r.ConstantItem(0.8f).Background("#76899c");
                    foreach (var n in left) r.RelativeItem().Element(x => Tooth(x, n, byTooth.GetValueOrDefault(n), text));
                });
            }
        });
    };

    /// <summary>The code printed under a tooth: its condition and, for a surface condition, the surfaces ("C·DO").</summary>
    internal static string Code(ToothFindingView f, Func<string, string> text) => f.Condition == ToothCondition.Sound ? ""
        : text($"dental.condition.abbr.{f.Condition}") + (f.Surfaces is { Length: > 0 } s ? "·" + s : "");

    static void Tooth(IContainer x, string n, ToothFindingView? f, Func<string, string> text) =>
        x.Border(0.5f).BorderColor(f is null ? "#dfe7ed" : "#cbd6e0").CornerRadius(2).PaddingVertical(1.5f).Column(t =>
        {
            t.Item().AlignCenter().Text(n).FontSize(6.5f).Bold().FontColor("#142536");
            t.Item().AlignCenter().Width(17).Height(17).Svg(Glyph(n, f));
            t.Item().AlignCenter().MinHeight(7).Text(f is null ? "–" : Code(f, text)).FontSize(5.5f).Bold().FontColor(f is null ? "#76899c" : "#142536");
        });

    /// <summary>
    /// One tooth as SVG. The surfaces are laid out for the upper right quadrant (buccal at the top, mesial towards the midline on the right); mirroring the
    /// tooth for its quadrant puts every surface on its side for the viewer facing the patient.
    /// </summary>
    public static string Glyph(string n, ToothFindingView? f)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        int q = n[0] - '0', i = n[1] - '0';
        var primary = q >= 5;
        var shape = ToothShapes.ByType[primary ? PrimaryType[i] : i];
        var (x, y, w, h) = (shape.X, shape.Y, shape.W, shape.H);
        var s = 32 / Math.Max(w, h) * (primary ? 0.86 : 1);
        var fx = q is 2 or 3 or 6 or 7 ? -1 : 1;
        var fy = q is 3 or 4 or 7 or 8 ? -1 : 1;
        double x1 = x + w / 3, x2 = x + 2 * w / 3, y1 = y + h / 3, y2 = y + 2 * h / 3, r = x + w, b = y + h;
        (string Surface, string Points)[] zones =
        [
            ("B", $"{N(x)},{N(y)} {N(r)},{N(y)} {N(x2)},{N(y1)} {N(x1)},{N(y1)}"), ("M", $"{N(r)},{N(y)} {N(r)},{N(b)} {N(x2)},{N(y2)} {N(x2)},{N(y1)}"),
            ("L", $"{N(x)},{N(b)} {N(x1)},{N(y2)} {N(x2)},{N(y2)} {N(r)},{N(b)}"), ("D", $"{N(x)},{N(y)} {N(x1)},{N(y1)} {N(x1)},{N(y2)} {N(x)},{N(b)}"),
            ("O", $"{N(x1)},{N(y1)} {N(x2)},{N(y1)} {N(x2)},{N(y2)} {N(x1)},{N(y2)}"),
        ];
        var (tone, fill, symbol) = f is null ? ("neutral", null, null) : Drawing[f.Condition];
        var look = Tones[tone];
        var paint = fill switch { "faded" => "#f1f5f8", null => null, _ => "url(#p)" };
        var whole = fill is "dots" or "faded";
        var px = 1 / s; // one unit of the 36 box, in the shape's own units
        var svg = new StringBuilder("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 36 36"><defs>""");
        if (fill is "hatch" or "solid" or "dots")
        {
            svg.Append(CultureInfo.InvariantCulture, $"""<pattern id="p" width="{N(5 * px)}" height="{N(5 * px)}" patternUnits="userSpaceOnUse"{(fill == "hatch" ? " patternTransform=\"rotate(45)\"" : "")}>""");
            svg.Append(CultureInfo.InvariantCulture, $"""<rect width="{N(5 * px)}" height="{N(5 * px)}" fill="{look.Bg}"/>""");
            if (fill == "hatch") svg.Append(CultureInfo.InvariantCulture, $"""<rect width="{N(2.4 * px)}" height="{N(5 * px)}" fill="{look.Text}"/>""");
            if (fill == "dots") svg.Append(CultureInfo.InvariantCulture, $"""<circle cx="{N(2.5 * px)}" cy="{N(2.5 * px)}" r="{N(1.1 * px)}" fill="{look.Text}"/>""");
            if (fill == "solid") svg.Append(CultureInfo.InvariantCulture, $"""<rect width="{N(5 * px)}" height="{N(5 * px)}" fill="{look.Border}" fill-opacity="0.45"/>""");
            svg.Append("</pattern>");
        }
        svg.Append(CultureInfo.InvariantCulture, $"""<clipPath id="c"><path d="{shape.Outline}"/></clipPath></defs>""");
        svg.Append(CultureInfo.InvariantCulture, $"""<g transform="translate(18 18) scale({N(fx * s)} {N(fy * s)}) translate({N(-(x + w / 2))} {N(-(y + h / 2))})">""");
        svg.Append(CultureInfo.InvariantCulture, $"""<path d="{shape.Outline}" fill="{(whole && paint is not null ? paint : "#ffffff")}"/>""");
        svg.Append(CultureInfo.InvariantCulture, $"""<g clip-path="url(#c)" stroke="#cbd6e0" stroke-width="{N(0.6 * px)}">""");
        foreach (var (surface, points) in zones)
            svg.Append(CultureInfo.InvariantCulture, $"""<polygon points="{points}" fill="{(!whole && paint is not null && f?.Surfaces?.Contains(surface, StringComparison.Ordinal) == true ? paint : "none")}"/>""");
        svg.Append(CultureInfo.InvariantCulture, $"""</g><path d="{shape.Line}" fill="none" stroke="#9fb3c8" stroke-width="{N(0.6 * px)}"/>""");
        svg.Append(CultureInfo.InvariantCulture, $"""<path d="{shape.Outline}" fill="none" stroke="{(f is null ? "#76899c" : "#142536")}" stroke-width="{N((f is null ? 1 : 1.4) * px)}"{(f is null ? $" stroke-dasharray=\"{N(3 * px)} {N(2 * px)}\"" : "")}/></g>""");
        if (symbol is not null) svg.Append(CultureInfo.InvariantCulture, $"""<path d="{Symbols[symbol]}" fill="none" stroke="{look.Text}" stroke-width="3" stroke-linecap="round"/>""");
        return svg.Append("</svg>").ToString();
    }
}
