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
/// so Hindi, Tamil and Arabic print shaped. On every page: the clinic's letterhead (the logo beside the name, address and contact line), the header
/// lines (the first two bold) over a rule, the body wrapped to the margins, and a footer. A signature image and a QR code follow the body, or the
/// paper ends with a <see cref="Closing"/>; each is kept whole on one page. A4 or A5.
/// A line with a character no embedded font has is refused with <see cref="PdfUnsupportedTextException"/>, never printed as empty boxes.
/// shortcut: text copied out of the PDF is garbled for Hindi and Arabic (the font's ToUnicode map for joined glyphs); the printed page is right and the
/// web view is the searchable, screen-reader copy. Tag the PDF (PDF/UA) when a PDF must be read aloud.
/// </summary>
public static class PlainPdf
{
    public sealed record Paper(string Name, float Width, float Height, float Margin);

    /// <summary>The clinic at the top of every page (#1365 F-068): its name, with the logo beside it and the address and contact line under it when there are any.</summary>
    public sealed record Letterhead(string Name, byte[]? Logo = null, string? Address = null, string? Contact = null);

    /// <summary>
    /// How a paper ends (#1365 F-068), kept whole and right under the body (blank space above a signature invites additions by hand): the QR code
    /// (rows, '1' dark) beside its caption and the address it opens, then the signing lines with the signature image under them. The address is
    /// printed as two lines, up to its last "/" and the code after it; a line too long for the paper shrinks to fit, so it is never wrapped or hyphenated.
    /// </summary>
    public sealed record Closing(IReadOnlyList<string> Qr, string Caption, string Url, IReadOnlyList<(string Text, bool Bold)> SignedBy, byte[]? Signature = null);

    public static readonly Paper A4 = new("A4", 595.28f, 841.89f, 56), A5 = new("A5", 419.53f, 595.28f, 36);
    const float FontSize = 10, LineHeight = 1.3f, Module = 2, LogoWidth = 150, LogoHeight = 54, SignatureHeight = 40;
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

    /// <summary>
    /// Letterhead, header, body and footer on every page; <paramref name="signature"/> right after the body (or after the body line numbered
    /// <paramref name="signatureAfter"/>), then <paramref name="qr"/>, the QR code's rows ('1' dark); or <paramref name="closing"/> in their place.
    /// Images are PNG or JPEG, scaled to fit.
    /// </summary>
    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Write(IReadOnlyList<string> header, IReadOnlyList<(string Text, bool Bold)> body, Func<int, int, string> footer, Paper? paper = null,
        IReadOnlyList<string>? qr = null, Letterhead? letterhead = null, byte[]? signature = null, int? signatureAfter = null, Closing? closing = null)
    {
        var p = paper ?? A4;
        var scale = p.Width / A4.Width; // the logo is as large on A5 as the narrower page allows
        var document = Document.Create(d => d.Page(page =>
        {
            page.Size(new PageSize(p.Width, p.Height));
            page.Margin(p.Margin);
            page.DefaultTextStyle(t => t.FontFamily(Fonts).FontSize(FontSize).LineHeight(LineHeight));
            page.Header().PaddingBottom(10).Column(c =>
            {
                if (letterhead is { } l)
                {
                    c.Item().Row(row =>
                    {
                        row.Spacing(12);
                        if (l.Logo is not null) row.AutoItem().MaxWidth(LogoWidth * scale).MaxHeight(LogoHeight * scale).Image(l.Logo).FitArea();
                        row.RelativeItem().Column(clinic =>
                        {
                            clinic.Item().Text(l.Name).Bold().FontSize(FontSize + 4);
                            foreach (var line in (l.Address ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Line(clinic, line, false);
                            if (l.Contact is not null) Line(clinic, l.Contact, false);
                        });
                    });
                    c.Item().PaddingVertical(4).LineHorizontal(0.5f);
                }
                for (var i = 0; i < header.Count; i++) Line(c, header[i], i < 2);
                c.Item().PaddingTop(4).LineHorizontal(0.5f);
            });
            page.Content().Column(c =>
            {
                var signAt = signatureAfter ?? body.Count - 1;
                for (var i = 0; i < body.Count; i++)
                {
                    if (body[i].Text.Length == 0) c.Item().Height(FontSize);
                    else Line(c, body[i].Text, body[i].Bold);
                    if (i == signAt && signature is not null) c.Item().Height(SignatureHeight).AlignLeft().Image(signature).FitHeight();
                }
                if (body.Count == 0 && signature is not null) c.Item().Height(SignatureHeight).AlignLeft().Image(signature).FitHeight();
                if (qr is { Count: > 0 }) c.Item().PaddingTop(FontSize).Width(qr.Count * Module).Height(qr.Count * Module).Svg(Svg(qr));
                if (closing is { } end) c.Item().ShowEntire().Column(foot =>
                {
                    foot.Item().PaddingBottom(8).LineHorizontal(0.5f);
                    foot.Item().Row(row =>
                    {
                        row.Spacing(12);
                        row.ConstantItem(end.Qr.Count * Module).Height(end.Qr.Count * Module).Svg(Svg(end.Qr));
                        row.RelativeItem().Column(words =>
                        {
                            Line(words, end.Caption, false);
                            var cut = end.Url.LastIndexOf('/') + 1;
                            // One line each, whatever the paper: a line too long for the width is scaled down, never broken.
                            foreach (var part in new[] { end.Url[..cut], end.Url[cut..] }.Where(x => x.Length > 0))
                                words.Item().Height(FontSize * LineHeight).ScaleToFit().Hyperlink(end.Url).Text(part);
                            words.Item().Height(FontSize);
                            foreach (var (text, bold) in end.SignedBy) Line(words, text, bold);
                            if (end.Signature is not null) words.Item().Height(SignatureHeight).AlignLeft().Image(end.Signature).FitHeight();
                        });
                    });
                });
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
