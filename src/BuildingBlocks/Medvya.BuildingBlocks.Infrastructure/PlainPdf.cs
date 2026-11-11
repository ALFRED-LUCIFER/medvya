using System.Text;
using QuestPDF.Drawing;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>The text holds characters none of the embedded fonts has (a script other than Latin, Devanagari, Tamil or Arabic, or an emoji).</summary>
public sealed class PdfUnsupportedTextException() : Exception("The text has characters the PDF fonts cannot show.");

/// <summary>
/// The server's text PDF (MDV-P2-013 signed note, MDV-P2-017 prescription; ADR-018): QuestPDF with embedded Noto Sans, Devanagari, Tamil and Arabic,
/// so Hindi, Tamil and Arabic print shaped. A header on every page (its first two lines bold) over a rule, the body wrapped to the margins, a footer
/// on every page and, optionally, a QR code after the body, kept whole on one page. A4 or A5.
/// A line with a character no embedded font has is refused with <see cref="PdfUnsupportedTextException"/>, never printed as empty boxes.
/// shortcut: text copied out of the PDF is garbled for Hindi and Arabic (the font's ToUnicode map for joined glyphs); the printed page is right and the
/// web view is the searchable, screen-reader copy. Tag the PDF (PDF/UA) when a PDF must be read aloud.
/// </summary>
public static class PlainPdf
{
    public sealed record Paper(string Name, float Width, float Height, float Margin);

    public static readonly Paper A4 = new("A4", 595.28f, 841.89f, 56), A5 = new("A5", 419.53f, 595.28f, 36);
    const float FontSize = 10, Module = 2;
    static readonly string[] Fonts = ["Noto Sans", "Noto Sans Devanagari", "Noto Sans Tamil", "Noto Sans Arabic"];

    static PlainPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community; // ADR-018: free under USD 1M revenue; re-check in the session handoff
        QuestPDF.Settings.UseSystemFonts = false; // only the embedded fonts: the same paper on a laptop and on App Service
        var assembly = typeof(PlainPdf).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Medvya.Fonts.", StringComparison.Ordinal)))
        {
            using var font = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFontFromStream(font);
        }
    }

    /// <summary>Header, body and footer on every page; <paramref name="qr"/> is the QR code's rows ('1' dark), drawn after the body.</summary>
    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Write(IReadOnlyList<string> header, IReadOnlyList<(string Text, bool Bold)> body, Func<int, int, string> footer, Paper? paper = null,
        IReadOnlyList<string>? qr = null)
    {
        var p = paper ?? A4;
        var document = Document.Create(d => d.Page(page =>
        {
            page.Size(new PageSize(p.Width, p.Height));
            page.Margin(p.Margin);
            page.DefaultTextStyle(t => t.FontFamily(Fonts).FontSize(FontSize).LineHeight(1.3f));
            page.Header().PaddingBottom(10).Column(c =>
            {
                for (var i = 0; i < header.Count; i++) Line(c, header[i], i < 2);
                c.Item().PaddingTop(4).LineHorizontal(0.5f);
            });
            page.Content().Column(c =>
            {
                foreach (var (text, bold) in body)
                    if (text.Length == 0) c.Item().Height(FontSize);
                    else Line(c, text, bold);
                if (qr is { Count: > 0 }) c.Item().PaddingTop(FontSize).Width(qr.Count * Module).Height(qr.Count * Module).Svg(Svg(qr));
            });
            page.Footer().Dynamic(new Footer(footer));
        }));
        try
        {
            return document.GeneratePdf();
        }
        catch (DocumentDrawingException e) when (e.Message.Contains("glyph", StringComparison.Ordinal))
        {
            throw new PdfUnsupportedTextException(); // its message quotes the text (PHI): never pass it on
        }
    }

    static void Line(ColumnDescriptor c, string text, bool bold)
    {
        var line = c.Item().Text(text);
        if (bold) line.Bold();
    }

    /// <summary>The QR code as one SVG path of unit squares, so it prints as vectors at any size.</summary>
    static string Svg(IReadOnlyList<string> qr)
    {
        var path = new StringBuilder();
        for (var r = 0; r < qr.Count; r++)
            for (var c = 0; c < qr[r].Length; c++)
                if (qr[r][c] == '1') path.Append(System.Globalization.CultureInfo.InvariantCulture, $"M{c} {r}h1v1h-1z");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {qr.Count} {qr.Count}\" shape-rendering=\"crispEdges\"><path d=\"{path}\" fill=\"#000\"/></svg>";
    }

    sealed class Footer(Func<int, int, string> text) : IDynamicComponent
    {
        public DynamicComponentComposeResult Compose(DynamicContext context) => new()
        {
            Content = context.CreateElement(e => e.PaddingTop(6).Text(text(context.PageNumber, context.TotalPages))),
            HasMoreContent = false,
        };
    }
}
