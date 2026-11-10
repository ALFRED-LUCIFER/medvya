using System.Globalization;
using System.Text;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>The text holds characters the built-in PDF font cannot show (another script than Latin).</summary>
public sealed class PdfUnsupportedTextException() : Exception("The text has characters the PDF font cannot show.");

/// <summary>
/// A text-only PDF writer with no dependency (MDV-P2-013; shared with the printed prescription, MDV-P2-017): Courier in Windows-1252, a header on
/// every page (its first two lines bold), the body wrapped on spaces, a footer on every page and, optionally, a QR code drawn as filled squares
/// after the body. A4 or A5.
/// shortcut: one built-in font (Courier, Windows-1252), so text in another script (Arabic, Devanagari, Tamil) throws
/// <see cref="PdfUnsupportedTextException"/> and the browser's print view is used instead; embed a Unicode font with shaping when a print must show it.
/// </summary>
public static class PlainPdf
{
    public sealed record Paper(string Name, double Width, double Height, double Margin);

    public static readonly Paper A4 = new("A4", 595.28, 841.89, 56), A5 = new("A5", 419.53, 595.28, 36);
    const double FontSize = 10, Leading = 14, CharWidth = FontSize * 0.6, BodyBottom = 70, FooterY = 40, Module = 2;
    static readonly Encoding WinAnsi = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    /// <summary>Characters per line on this paper: Courier is 0.6 em wide (A4: 80, inside the 483 pt between the margins).</summary>
    public static int Columns(Paper paper) => (int)((paper.Width - 2 * paper.Margin) / CharWidth);

    /// <summary>
    /// Header and body wrapped at <see cref="Columns"/> on spaces (a longer word is cut), so a long name never pushes an identifier off the page;
    /// paginated, with the header and footer on every page. <paramref name="qr"/> is the QR code's rows ('1' dark), drawn after the body.
    /// </summary>
    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Write(IReadOnlyList<string> header, IReadOnlyList<(string Text, bool Bold)> body, Func<int, int, string> footer, Paper? paper = null,
        IReadOnlyList<string>? qr = null)
    {
        var p = paper ?? A4;
        var columns = Columns(p);
        var headerTop = p.Height - 41.89; // 800 on A4
        var head = header.SelectMany((h, i) => Wrap(h, columns).Select(w => (Text: w, Bold: i < 2))).ToList();
        var bodyTop = headerTop - head.Count * Leading - 16;
        var lines = body.SelectMany(l => Wrap(l.Text, columns).Select(w => (Text: w, l.Bold))).ToList();
        var perPage = (int)((bodyTop - BodyBottom) / Leading) + 1;
        var qrAt = -1;
        if (qr is { Count: > 0 })
        {
            // The code takes blank lines of its own, kept on one page.
            var needed = (int)Math.Ceiling(qr.Count * Module / Leading) + 1;
            if (lines.Count % perPage != 0 && perPage - lines.Count % perPage < needed) lines.AddRange(Enumerable.Repeat(("", false), perPage - lines.Count % perPage));
            qrAt = lines.Count;
            lines.AddRange(Enumerable.Repeat(("", false), needed));
        }
        var pages = lines.Chunk(perPage).ToList();
        if (pages.Count == 0) pages.Add([]);

        var objects = new List<byte[]> { Ascii("<< /Type /Catalog /Pages 2 0 R /Lang (en) >>"), Array.Empty<byte>(), Font("Courier"), Font("Courier-Bold") };
        var kids = new List<int>();
        for (var n = 0; n < pages.Count; n++)
        {
            var content = new StringBuilder();
            void Line(string text, bool bold, double y) =>
                content.Append(CultureInfo.InvariantCulture, $"BT /F{(bold ? 2 : 1)} {FontSize} Tf {p.Margin} {y:0.##} Td (").Append(Escape(text)).Append(") Tj ET\n");
            for (var h = 0; h < head.Count; h++) Line(head[h].Text, head[h].Bold, headerTop - h * Leading);
            var rule = headerTop - head.Count * Leading + 4;
            content.Append(CultureInfo.InvariantCulture, $"0.5 w {p.Margin} {rule:0.##} m {p.Width - p.Margin:0.##} {rule:0.##} l S\n");
            for (var i = 0; i < pages[n].Length; i++) Line(pages[n][i].Text, pages[n][i].Bold, bodyTop - i * Leading);
            if (qrAt >= 0 && qrAt / perPage == n)
            {
                var top = bodyTop - qrAt % perPage * Leading + FontSize;
                for (var r = 0; r < qr!.Count; r++)
                    for (var c = 0; c < qr[r].Length; c++)
                        if (qr[r][c] == '1') content.Append(CultureInfo.InvariantCulture, $"{p.Margin + c * Module:0.##} {top - (r + 1) * Module:0.##} {Module} {Module} re\n");
                content.Append("f\n");
            }
            Line(footer(n + 1, pages.Count), false, FooterY);
            var stream = Ascii(content.ToString());
            objects.Add(Ascii($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Ascii("endstream")).ToArray());
            objects.Add(Ascii(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {p.Width} {p.Height}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {objects.Count} 0 R >>")));
            kids.Add(objects.Count);
        }
        objects[1] = Ascii($"<< /Type /Pages /Kids [{string.Join(' ', kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>");

        using var pdf = new MemoryStream();
        pdf.Write(Ascii("%PDF-1.4\n"));
        pdf.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // binary marker comment, so transfer tools treat the file as binary
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            pdf.Write(Ascii($"{i + 1} 0 obj\n"));
            pdf.Write(objects[i]);
            pdf.Write(Ascii("\nendobj\n"));
        }
        var xref = pdf.Position;
        pdf.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var o in offsets) pdf.Write(Ascii($"{o:D10} 00000 n \n"));
        pdf.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return pdf.ToArray();
    }

    static byte[] Font(string name) => Ascii($"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding /WinAnsiEncoding >>");

    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    static IEnumerable<string> Wrap(string text, int columns)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\t', ' ').Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > columns) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                var rest = word;
                while (line.Length + rest.Length > columns)
                {
                    var fits = columns - line.Length;
                    yield return line.Append(rest[..fits]).ToString();
                    (rest, line) = (rest[fits..], line.Clear());
                }
                line.Append(rest);
            }
            yield return line.ToString();
        }
    }

    /// <summary>A PDF string body: Windows-1252 bytes, with the delimiters escaped and every byte outside printable ASCII as an octal escape.</summary>
    static string Escape(string text)
    {
        byte[] bytes;
        try
        {
            bytes = WinAnsi.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            throw new PdfUnsupportedTextException();
        }
        var s = new StringBuilder();
        foreach (var b in bytes)
            if (b is (byte)'(' or (byte)')' or (byte)'\\') s.Append('\\').Append((char)b);
            else if (b is < 0x20 or > 0x7E) s.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else s.Append((char)b);
        return s.ToString();
    }
}
