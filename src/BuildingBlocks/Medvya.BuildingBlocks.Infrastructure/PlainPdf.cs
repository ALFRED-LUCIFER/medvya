using System.Text;
using QuestPDF.Drawing.Exceptions;
using QuestPDF.Drawing;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>The text holds characters none of the embedded fonts has (a script other than Latin, Devanagari, Tamil or Arabic, or an emoji).</summary>
public sealed class PdfUnsupportedTextException() : Exception("The text has characters the PDF fonts cannot show.");

/// <summary>
/// The server's papers (MDV-P2-013 signed note, MDV-P2-017 prescription, MDV-P2-018/019 slip and letter; ADR-018) in the print family layout
/// (MDV-P1-111, docs/02-design/print-family): on every page a masthead (logo, clinic, address and phone; the doctor block at the end), a teal rule, the
/// title and number, and the patient row; then rows with small labels hanging in the start margin, a signature box, and on every page a footer with the
/// verify QR code and the page number. QuestPDF with embedded Manrope, then Noto Sans, Devanagari, Tamil and Arabic, so Hindi, Tamil and Arabic print shaped.
/// Text with a character no embedded font has is refused with <see cref="PdfUnsupportedTextException"/>, never printed as empty boxes.
/// shortcut: text copied out of the PDF is garbled for Hindi and Arabic (the font's ToUnicode map for joined glyphs); the printed page is right and the
/// web view is the searchable, screen-reader copy. Tag the PDF (PDF/UA) when a PDF must be read aloud.
/// shortcut: the design's monospace numbers and links print in Manrope (no monospace font is embedded); embed one if pharmacists misread a number.
/// </summary>
public static class PlainPdf
{
    /// <summary>A page size and its rhythm, in points (the design's .sheet and .a5 rules): A5 keeps the type sizes and tightens the spacing.</summary>
    public sealed record Paper(string Name, float Width, float Height, float Margin, float Top, float Bottom, float Label, float Gap, float RowPad, float ItemGap,
        float MetaGap, float Qr, float SignWidth, float SignGap, float SignHeight, float FootGap, float FootPad);

    const float Mm = 2.8346f;
    public static readonly Paper A4 = new("A4", 595.28f, 841.89f, 19 * Mm, 18 * Mm, 14 * Mm, 27 * Mm, 5 * Mm, 2.6f * Mm, 3.4f * Mm, 5 * Mm, 21 * Mm, 62 * Mm, 6 * Mm, 11 * Mm, 6 * Mm, 3.5f * Mm);
    public static readonly Paper A5 = new("A5", 419.53f, 595.28f, 12 * Mm, 11 * Mm, 9 * Mm, 21 * Mm, 4 * Mm, 1.7f * Mm, 2.2f * Mm, 3 * Mm, 18 * Mm, 56 * Mm, 2 * Mm, 9 * Mm, 3 * Mm, 2.5f * Mm);

    // Medvya tokens (packages/tokens/src/tokens.json), as the design uses them on white paper.
    const string Brand950 = "#102a43", Brand700 = "#0f766e", Ink900 = "#142536", Ink700 = "#334e68", Ink600 = "#506579", Ink200 = "#cbd6e0", Line = "#dfe7ed",
        Danger700 = "#b42333", Danger200 = "#f3b9c0", Danger50 = "#fdecee";
    const string Shield = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" fill="none" stroke="#b42333" stroke-width="2.4" stroke-linejoin="round"/><path d="M12 8v4M12 16h.01" stroke="#b42333" stroke-width="2.4" stroke-linecap="round"/></svg>""";
    static readonly string[] Fonts = ["Manrope", "Noto Sans", "Noto Sans Devanagari", "Noto Sans Tamil", "Noto Sans Arabic"];

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
    /// The masthead: the clinic (logo PNG or JPEG, when uploaded) with its address, phone and registration line (already worded, #1365 F-068) at the
    /// start; the doctor and their lines at the end.
    /// </summary>
    public sealed record Masthead(string Clinic, string? Address, string? Phone, byte[]? Logo, string Doctor, IReadOnlyList<string> DoctorLines, string? Registration = null);

    /// <summary>The meta row under the rule: the paper's title, then its number (when it has one) and the detail (version, date).</summary>
    public sealed record Meta(string Title, string? Number, string Detail);

    /// <summary>A labelled row: the label hangs in the start margin, the value's parts (one per numbered item, else one) fill the column.</summary>
    public sealed record Row(string Label, params Action<IContainer>[] Parts);

    /// <summary>The signature box: the image (when uploaded) over a line, the name, then its lines (registration, when). Without an image, <paramref name="NoSignatureNeeded"/>
    /// (when given) is printed in place of the space left to sign by hand.</summary>
    public sealed record Signature(byte[]? Image, string Name, IReadOnlyList<string> Lines, string? NoSignatureNeeded = null);

    /// <summary>The footer on every page: the QR code's rows ('1' dark; none: no code), the check line, its link and hash, and the page cell (number, page X of Y).</summary>
    public sealed record Footer(IReadOnlyList<string>? Qr, string Check, string? Link, string? Hash, string? Number, Func<int, int, string> Page);

    /// <summary>A numbered item: the name (a medicine in capitals), lighter detail after it, lines under it, and boxed notices (print requirements).</summary>
    public sealed record Item(string Name, string? Detail, IReadOnlyList<string> Lines, IReadOnlyList<string> Boxes);

    public enum Tone { Body, Strong, Name, Muted, Small }

    /// <summary>
    /// Masthead, rule, meta and the patient row head every page (the patient's identifiers are on every page), then the rows, the signature box (after row
    /// <paramref name="signAfter"/>, else at the foot of the last page) and the footer on every page.
    /// </summary>
    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Write(Paper paper, Masthead mast, Meta meta, Row patient, IReadOnlyList<Row> rows, Signature sign, Footer footer, int? signAfter = null)
    {
        var document = Document.Create(d => d.Page(page =>
        {
            page.Size(new PageSize(paper.Width, paper.Height));
            page.MarginHorizontal(paper.Margin);
            page.MarginTop(paper.Top);
            page.MarginBottom(paper.Bottom);
            page.DefaultTextStyle(t => t.FontFamily(Fonts).FontSize(10).LineHeight(1.38f).Medium().FontColor(Ink900));
            page.Header().Column(c =>
            {
                c.Item().Element(e => Mast(e, mast));
                c.Item().PaddingTop(4.5f * Mm).LineHorizontal(2).LineColor(Brand700);
                c.Item().PaddingTop(4).PaddingBottom(paper.MetaGap).Row(r =>
                {
                    r.Spacing(4 * Mm);
                    r.AutoItem().Text(meta.Title.ToUpperInvariant()).FontSize(7.6f).ExtraBold().FontColor(Brand700).LetterSpacing(0.1f);
                    r.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7.6f).FontColor(Ink600));
                        if (meta.Number is { } number) t.Span($"{number} · ").FontColor(Ink900);
                        t.Span(meta.Detail);
                    });
                });
                c.Item().Element(e => Hang(e, paper, patient, first: true));
            });
            page.Content().Column(c =>
            {
                var signAt = Math.Min(signAfter ?? rows.Count - 1, rows.Count - 1);
                for (var i = 0; i < rows.Count; i++)
                {
                    var (row, atFoot) = (rows[i], i == rows.Count - 1);
                    c.Item().Element(e => Hang(e, paper, row, first: false, i == signAt ? sign : null, atFoot));
                }
                if (rows.Count == 0) c.Item().ExtendVertical().AlignBottom().AlignRight().Element(e => Sign(e, paper, sign));
            });
            page.Footer().PaddingTop(paper.FootGap).Dynamic(new Foot(paper, footer));
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

    static void Mast(IContainer e, Masthead m) => e.Row(r =>
    {
        r.Spacing(6 * Mm);
        r.RelativeItem().Row(who =>
        {
            who.Spacing(3.5f * Mm);
            if (m.Logo is not null) who.ConstantItem(13 * Mm).Height(13 * Mm).AlignMiddle().Image(m.Logo).FitArea();
            who.RelativeItem().AlignMiddle().Column(c =>
            {
                c.Item().Text(m.Clinic).FontSize(15).LineHeight(1.15f).ExtraBold().FontColor(Brand950);
                var address = string.Join(" · ", new[] { m.Address, m.Phone, m.Registration }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (address.Length > 0) c.Item().PaddingTop(2).Text(address).FontSize(7.6f).FontColor(Ink600);
            });
        });
        r.AutoItem().MaxWidth(70 * Mm).Column(c =>
        {
            c.Item().AlignRight().Text(m.Doctor).FontSize(11).ExtraBold();
            foreach (var line in m.DoctorLines) c.Item().AlignRight().Text(line).FontSize(7.6f).FontColor(Ink700).AlignRight();
        });
    });

    /// <summary>
    /// One labelled row. With <paramref name="sign"/>, the signature box moves with the row's last part (its last item), never alone onto a page: a
    /// signature-only page can be stapled to another paper. <paramref name="atFoot"/>: the box sits at the page foot, as the design's margin-top: auto.
    /// </summary>
    static void Hang(IContainer e, Paper p, Row row, bool first, Signature? sign = null, bool atFoot = false) =>
        e.Element(x => sign is not null && row.Parts.Length == 1 ? x.PreventPageBreak() : x) // a one-part row moves whole with the box, label included
        .Element(x => first ? x : x.BorderTop(0.5f).BorderColor(Line)).PaddingVertical(p.RowPad).Row(r =>
        {
            r.Spacing(p.Gap);
            r.ConstantItem(p.Label).PaddingTop(2.2f).Text(row.Label.ToUpperInvariant()).FontSize(6.8f).Bold().FontColor(Ink600).LetterSpacing(0.08f);
            r.RelativeItem().Column(c =>
            {
                c.Spacing(p.ItemGap);
                for (var i = 0; i < row.Parts.Length - 1; i++) c.Item().Element(row.Parts[i]);
                var last = row.Parts[^1];
                if (sign is null) c.Item().Element(last);
                else c.Item().PreventPageBreak().Column(block =>
                {
                    block.Item().Element(last);
                    (atFoot ? block.Item().ExtendVertical().AlignBottom() : block.Item()).AlignRight().Element(x => Sign(x, p, sign));
                });
            });
        });

    static void Sign(IContainer e, Paper p, Signature s) => e.PaddingTop(p.SignGap).Width(p.SignWidth).ShowEntire().Column(c =>
    {
        // No image uploaded: the space stays blank, so the doctor signs by hand above the line, unless the paper says it needs no signature.
        if (s.Image is null && s.NoSignatureNeeded is { } note) c.Item().MinHeight(p.SignHeight).AlignBottom().PaddingBottom(3).Text(note).FontSize(7.6f).FontColor(Ink700);
        else if (s.Image is null) c.Item().Height(p.SignHeight);
        else c.Item().Height(p.SignHeight).AlignLeft().AlignBottom().Image(s.Image).FitHeight();
        c.Item().BorderTop(0.7f).BorderColor(Ink900).PaddingTop(3).Column(box =>
        {
            box.Item().Text(s.Name).ExtraBold();
            foreach (var line in s.Lines) box.Item().Text(line).FontSize(7.6f).FontColor(Ink700);
        });
    });

    /// <summary>Lines of text in the value column, each in its tone.</summary>
    public static Action<IContainer> Lines(params (string Text, Tone Tone)[] lines) => e => e.Column(c =>
    {
        foreach (var (text, tone) in lines)
        {
            var t = c.Item().Text(text);
            _ = tone switch
            {
                Tone.Strong => t.ExtraBold(),
                Tone.Name => t.FontSize(11.5f).ExtraBold(),
                Tone.Muted => t.FontColor(Ink700),
                Tone.Small => t.FontSize(7.6f).FontColor(Ink600),
                _ => t,
            };
        }
    });

    /// <summary>Paragraphs of a strong part and a lighter rest, such as a medicine to stop and its reason.</summary>
    public static Action<IContainer> Pairs(IEnumerable<(string Strong, string? Light)> pairs) => e => e.Column(c =>
    {
        foreach (var (strong, light) in pairs)
            c.Item().Text(t =>
            {
                t.Span(strong).ExtraBold();
                if (light is not null) t.Span($" {light}").FontColor(Ink700);
            });
    });

    /// <summary>The allergy block: danger fill and border, a shield and the word as well as the colour (never colour alone), then the substances.</summary>
    public static Action<IContainer> Allergy(string word, string list) => e => e.AlignLeft()
        .Background(Danger50).Border(0.8f).BorderColor(Danger200).CornerRadius(3).PaddingVertical(3.5f).PaddingHorizontal(7).Row(r =>
        {
            r.Spacing(1.8f * Mm);
            r.AutoItem().AlignMiddle().Width(11).Height(11).Svg(Shield);
            r.AutoItem().AlignMiddle().Text(t =>
            {
                t.Span($"{word} ").ExtraBold().FontColor(Danger700);
                t.Span(list).Bold();
            });
        });

    /// <summary>Numbered items: a teal number, the name with its detail in lighter type, the lines under it and each notice boxed under its own item.</summary>
    public static Action<IContainer>[] Items(IReadOnlyList<Item> items) => [.. items.Select((item, i) => (Action<IContainer>)(e =>
    {
        var n = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.ShowEntire().Row(r => // an item never splits across pages: its instruction stays with its name
        {
            r.ConstantItem(14).Text(n).ExtraBold().FontColor(Brand700);
            r.RelativeItem().Column(body =>
            {
                body.Item().Text(t =>
                {
                    t.Span(item.Name).FontSize(11).ExtraBold().LetterSpacing(0.01f);
                    if (!string.IsNullOrEmpty(item.Detail)) t.Span($" {item.Detail}").FontSize(9.5f).SemiBold().FontColor(Ink700);
                });
                foreach (var line in item.Lines) body.Item().PaddingTop(1).Text(line);
                foreach (var box in item.Boxes)
                    body.Item().PaddingTop(2.5f).Border(0.6f).BorderColor(Ink200).CornerRadius(2.5f).PaddingVertical(3.5f).PaddingHorizontal(6)
                        .Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(7.2f).LineHeight(1.35f).FontColor(Ink700));
                            var cut = box.IndexOf(". ", StringComparison.Ordinal) + 1; // the first sentence (e.g. "Schedule H drug.") in bold
                            if (cut > 0) t.Span(box[..cut]).Bold().FontColor(Ink900);
                            t.Span(box[cut..]);
                        });
            });
        });
    }))];

    /// <summary>The QR code as one SVG path of unit squares, so it prints as vectors at any size.</summary>
    static string Svg(IReadOnlyList<string> qr)
    {
        var path = new StringBuilder();
        for (var r = 0; r < qr.Count; r++)
            for (var c = 0; c < qr[r].Length; c++)
                if (qr[r][c] == '1') path.Append(System.Globalization.CultureInfo.InvariantCulture, $"M{c} {r}h1v1h-1z");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {qr.Count} {qr.Count}\" shape-rendering=\"crispEdges\"><path d=\"{path}\" fill=\"#000\"/></svg>";
    }

    sealed class Foot(Paper p, Footer f) : IDynamicComponent
    {
        readonly string? qr = f.Qr is { Count: > 0 } rows ? Svg(rows) : null;

        public DynamicComponentComposeResult Compose(DynamicContext context) => new()
        {
            Content = context.CreateElement(e => e.BorderTop(0.5f).BorderColor(Line).PaddingTop(p.FootPad).DefaultTextStyle(s => s.FontSize(7.4f).FontColor(Ink600)).Row(r =>
            {
                r.Spacing(4 * Mm);
                if (qr is not null) r.ConstantItem(p.Qr).Height(p.Qr).Svg(qr);
                r.RelativeItem().AlignMiddle().Column(c =>
                {
                    c.Item().Text(f.Check).FontSize(8).Bold().FontColor(Ink900);
                    if (f.Link is not null) c.Item().Text(f.Link).FontColor(Ink900);
                    if (f.Hash is not null) c.Item().Text(f.Hash).FontSize(6.4f);
                });
                r.AutoItem().AlignMiddle().Column(c =>
                {
                    if (f.Number is not null) c.Item().AlignRight().Text(f.Number).FontColor(Ink900);
                    c.Item().AlignRight().Text(f.Page(context.PageNumber, context.TotalPages));
                });
            })),
            HasMoreContent = false,
        };
    }
}
