using System.Globalization;
using System.Text.Json;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Patients.Application;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The signed note as an A4 PDF (MDV-P2-013, E-06): the patient's name, MRN and date of birth at the top of every page, version 1 with its signer
/// block, what was recorded at signing (MDV-P2-049: vitals, allergies, diagnoses, medicines, as the print view shows them), then each addendum in
/// order with its reason and signer, and "Corrections are added as signed addenda" at the foot of every page.
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

    /// <summary>One signed version as printed: the signer's name and licence are looked up by the caller (ids only are stored).</summary>
    public sealed record Version(SignedNoteVersion Row, string SignerName, string? SignerLicence = null);

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
            .. Record(original.Row.AtSigning, zone),
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
            original.SignerLicence is { } licence ? Text("notePrint.doctor", ("name", original.SignerName), ("licence", licence)) : original.SignerName,
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

    /// <summary>
    /// MDV-P2-049: the record kept at signing, in the print view's words and order. A note signed before it was kept says so; today's chart is
    /// never read in its place. Allergies: listed, no known allergies, or not recorded (never "none").
    /// </summary>
    static IEnumerable<(string, bool)> Record(NoteAtSigning? r, TimeZoneInfo zone)
    {
        if (r is null) return [(Text("notePrint.notCaptured"), false), ("", false)];
        string Source(VitalAtSigning v)
        {
            var at = TimeZoneInfo.ConvertTime(v.TakenAt, zone);
            (string, string)[] when = [("date", at.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)), ("time", at.ToString("HH:mm", CultureInfo.InvariantCulture))];
            return v.RecordedByName is { } name ? Text("noteAssist.source", [("name", name), .. when]) : Text("noteAssist.sourceNoName", when);
        }
        string Vital(VitalAtSigning v)
        {
            var line = Text("noteAssist.vitalLine", ("label", Text($"noteAssist.vital.{v.Code}")), ("value", (v.ValueHundredths / 100m).ToString("0.##", CultureInfo.InvariantCulture)),
                ("unit", Observation.Units(v.Code)), ("source", Source(v)));
            return v.Flag is { } flag ? Text("notePrint.flagged", ("line", line), ("flag", Text($"noteAssist.flag.{flag}"))) : line;
        }
        string Allergy(AllergyAtSigning a) => Text(a.Reaction is null ? "notePrint.allergy" : "notePrint.allergyReaction", ("substance", a.Substance), ("reaction", a.Reaction ?? ""),
            ("status", Text($"patient360.allergyStatus.{a.Status}")), ("source", Text($"timeline.source.{a.Source}")));
        string Diagnosis(DiagnosisAtSigning d) => Text("notePrint.diagnosis", ("description", d.Description), ("code", d.Icd10Code ?? Text("problems.uncoded")),
            ("verification", Text($"problems.verification.{d.Verification}")), ("source", Text($"timeline.source.{d.Source}")));
        string Medicine(MedicineAtSigning m) => Text("notePrint.medicine", ("medicine", m.Dose is null ? m.Name : $"{m.Name} {m.Dose}"));
        return [
            (Text("notePrint.atSigning"), true),
            (Text("noteAssist.vitals.title"), true),
            .. r.Vitals.Count == 0 ? [(Text("notePrint.vitalsEmpty"), false)] : r.Vitals.Select(v => (Vital(v), false)),
            (Text("patient360.allergies"), true),
            .. r.Allergies.Count > 0 ? r.Allergies.Select(a => (Allergy(a), false)) : [(Text(r.AllergiesRecorded ? "banner.noAllergies" : "banner.allergiesNotRecorded"), false)],
            (Text("problems.title"), true),
            .. r.Diagnoses.Count == 0 ? [(Text("problems.empty"), false)] : r.Diagnoses.Select(d => (Diagnosis(d), false)),
            (Text("rxPrint.medicines"), true),
            .. r.Medicines.Count == 0 ? [(Text("notePrint.medicinesEmpty"), false)] : r.Medicines.Select(m => (Medicine(m), false)),
            ("", false),
        ];
    }

    /// <summary>A text section as written; a structured one (MDV-P2-042) a line per answered item in words. An unanswered item is not printed.</summary>
    static IEnumerable<(string, bool)> Lines(NoteSection section, string text) => section.Kind == SectionKind.Text ? [(text, false)]
        : StructuredAnswers.Read(section, text).Select(a => (Text(a.Note is null ? "noteTemplates.answerLine" : "noteTemplates.answerLineNote", ("item", Text($"noteTemplates.item.{a.Item}")),
            ("answer", Text($"noteTemplates.answer.{a.Value}")), ("note", a.Note ?? "")), false));

    static string Text(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
}
