using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Medvya.IntegrationTests;

/// <summary>Reads a server PDF back (PlainPdf, ADR-018): each page's text (whitespace collapsed), its width, whether every glyph is inside the margins, and how many drawn shapes and images it has.</summary>
static partial class PdfPages
{
    public sealed record Page(string Text, double Width, bool InsideMargins, int Shapes, int Images);

    public static IReadOnlyList<Page> Read(byte[] pdf, double margin)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Select(p => new Page(
            Space().Replace(ContentOrderTextExtractor.GetText(p), " ").Trim(),
            p.Width,
            p.Letters.All(l => l.BoundingBox.Left >= margin - 1 && l.BoundingBox.Right <= p.Width - margin + 1),
            p.Paths.Sum(path => path.Count),
            p.GetImages().Count())).ToList();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
