using System.Globalization;
using System.Text;
using System.Text.Json;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Patients.Application;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The signed note as an A4 PDF (MDV-P2-013, E-06): the patient's name, MRN and date of birth at the top of every page, version 1 with its signer
/// block, then each addendum in order with its reason and signer, and "Corrections are added as signed addenda" at the foot of every page.
/// Copy comes from the shared English catalogue (packages/i18n/messages/en.json); the server never invents wording.
/// shortcut: one built-in PDF font (Courier, Windows-1252), so text in another script (Arabic, Devanagari) is refused with
/// <see cref="NotePdfUnsupportedTextException"/> and the browser's print view is used instead; embed a Unicode font with shaping (and print
/// in the reader's language) when a pilot clinic writes notes in another script.
/// </summary>
public static class SignedNotePdfDocument
{
    const double Width = 595.28, Height = 841.89, Margin = 56, FontSize = 10, Leading = 14;
    const int Columns = 80; // Courier is 0.6 em per character: 80 x 6 pt = 480 pt, inside the 483 pt between the margins
    const double HeaderTop = 800, BodyTop = 756, BodyBottom = 70, FooterY = 40;
    static readonly int LinesPerPage = (int)((BodyTop - BodyBottom) / Leading) + 1;
    static readonly Encoding WinAnsi = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
    static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(SignedNotePdfDocument).Assembly.GetManifestResourceStream("Medvya.Encounters.messages.en.json")
            ?? throw new InvalidOperationException("The English message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    /// <summary>One signed version as printed: the signer's name is looked up by the caller (ids only are stored).</summary>
    public sealed record Version(SignedNoteVersion Row, string SignerName);

    /// <exception cref="NotePdfUnsupportedTextException"/>
    public static byte[] Render(PatientSummary patient, string facilityName, TimeZoneInfo zone, NoteTemplate template, IReadOnlyList<Version> versions)
    {
        var original = versions[0];
        string When(DateTimeOffset at) => Text("notePdf.when", ("date", TimeZoneInfo.ConvertTime(at, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)), ("zone", zone.Id));
        List<(string Text, bool Bold)> body =
        [
            (Text("notePdf.signedBy", ("version", "1"), ("name", original.SignerName), ("when", When(original.Row.SignedAt))), true),
            (Text("notePdf.hash", ("hash", original.Row.ContentHash)), false),
            ("", false),
        ];
        foreach (var section in template.Sections)
            if (original.Row.Sections.TryGetValue(section.Id, out var text))
                body.AddRange([(Text($"noteTemplates.section.{section.Id}"), true), (text, false), ("", false)]);
        foreach (var a in versions.Skip(1))
            body.AddRange([
                (Text("notePdf.addendum", ("version", a.Row.VersionNo.ToString(CultureInfo.InvariantCulture)), ("name", a.SignerName), ("when", When(a.Row.SignedAt))), true),
                (Text("notePdf.reason", ("reason", a.Row.Reason ?? "")), false),
                (a.Row.Sections[SignedNoteVersion.AddendumSection], false),
                (Text("notePdf.hash", ("hash", a.Row.ContentHash)), false),
                ("", false),
            ]);
        string[] header =
        [
            Text("notePdf.patient", ("name", patient.FullName), ("mrn", patient.Mrn), ("dob", patient.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))),
            Text("notePdf.title", ("facility", facilityName), ("version", versions[^1].Row.VersionNo.ToString(CultureInfo.InvariantCulture))),
        ];
        return Write(header, body, (page, pages) => Text("notePdf.footer", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
    }

    static string Text(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));

    /// <summary>Lines wrapped at <see cref="Columns"/> on spaces (a longer word is cut), paginated, with the header and footer on every page.</summary>
    internal static byte[] Write(IReadOnlyList<string> header, IReadOnlyList<(string Text, bool Bold)> body, Func<int, int, string> footer)
    {
        var lines = body.SelectMany(l => Wrap(l.Text).Select(w => (Text: w, l.Bold))).ToList();
        var pages = lines.Chunk(LinesPerPage).ToList();
        if (pages.Count == 0) pages.Add([]);

        var objects = new List<byte[]> { Ascii("<< /Type /Catalog /Pages 2 0 R /Lang (en) >>"), Array.Empty<byte>(), Font("Courier"), Font("Courier-Bold") };
        var kids = new List<int>();
        for (var p = 0; p < pages.Count; p++)
        {
            var content = new StringBuilder();
            void Line(string text, bool bold, double y) =>
                content.Append(CultureInfo.InvariantCulture, $"BT /F{(bold ? 2 : 1)} {FontSize} Tf {Margin} {y:0.##} Td (").Append(Escape(text)).Append(") Tj ET\n");
            for (var h = 0; h < header.Count; h++) Line(header[h], h == 0, HeaderTop - h * Leading);
            content.Append(CultureInfo.InvariantCulture, $"0.5 w {Margin} {HeaderTop - header.Count * Leading + 4:0.##} m {Width - Margin:0.##} {HeaderTop - header.Count * Leading + 4:0.##} l S\n");
            for (var i = 0; i < pages[p].Length; i++) Line(pages[p][i].Text, pages[p][i].Bold, BodyTop - i * Leading);
            Line(footer(p + 1, pages.Count), false, FooterY);
            var stream = Ascii(content.ToString());
            objects.Add(Ascii($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Ascii("endstream")).ToArray());
            objects.Add(Ascii(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Width} {Height}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {objects.Count} 0 R >>")));
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

    static IEnumerable<string> Wrap(string text)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace('\t', ' ').Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > Columns) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                var rest = word;
                while (line.Length + rest.Length > Columns)
                {
                    var fits = Columns - line.Length;
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
            throw new NotePdfUnsupportedTextException();
        }
        var s = new StringBuilder();
        foreach (var b in bytes)
            if (b is (byte)'(' or (byte)')' or (byte)'\\') s.Append('\\').Append((char)b);
            else if (b is < 0x20 or > 0x7E) s.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else s.Append((char)b);
        return s.ToString();
    }
}
