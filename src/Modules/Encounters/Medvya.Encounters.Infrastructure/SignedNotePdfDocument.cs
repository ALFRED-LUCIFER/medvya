using System.Globalization;
using System.Text.Json;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Patients.Application;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The signed note as an A4 PDF (MDV-P2-013, E-06): the patient's name, MRN and date of birth at the top of every page, version 1 with its signer
/// block, then each addendum in order with its reason and signer, and "Corrections are added as signed addenda" at the foot of every page.
/// Copy comes from the shared English catalogue (packages/i18n/messages/en.json); the server never invents wording.
/// Written by <see cref="PlainPdf"/>: Hindi, Tamil and Arabic print shaped; text no embedded font has is refused with <see cref="NotePdfUnsupportedTextException"/>.
/// </summary>
public static class SignedNotePdfDocument
{
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
                body.AddRange([(Text($"noteTemplates.section.{section.Id}"), true), .. Lines(section, text), ("", false)]);
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
            patient.FullName,
            Text("notePdf.patient", ("mrn", patient.Mrn), ("dob", patient.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))),
            Text("notePdf.title", ("facility", facilityName), ("version", versions[^1].Row.VersionNo.ToString(CultureInfo.InvariantCulture))),
        ];
        try
        {
            return PlainPdf.Write(header, body, (page, pages) => Text("notePdf.footer", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        }
        catch (PdfUnsupportedTextException)
        {
            throw new NotePdfUnsupportedTextException();
        }
    }

    /// <summary>A text section as written; a structured one (MDV-P2-042) a line per answered item in words. An unanswered item is not printed.</summary>
    static IEnumerable<(string, bool)> Lines(NoteSection section, string text) => section.Kind == SectionKind.Text ? [(text, false)]
        : StructuredAnswers.Read(section, text).Select(a => (Text(a.Note is null ? "noteTemplates.answerLine" : "noteTemplates.answerLineNote", ("item", Text($"noteTemplates.item.{a.Item}")),
            ("answer", Text($"noteTemplates.answer.{a.Value}")), ("note", a.Note ?? "")), false));

    static string Text(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
}
