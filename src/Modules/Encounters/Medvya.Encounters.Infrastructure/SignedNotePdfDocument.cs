using System.Globalization;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using Tone = Medvya.BuildingBlocks.Infrastructure.PlainPdf.Tone;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The signed note as an A4 PDF (MDV-P2-013, E-06) in the print family frame (MDV-P1-111): the clinic and the version 1 signer in the masthead, the
/// patient's name, MRN and date of birth on every page, then labelled rows: version 1's signer and hash, what was recorded at signing (MDV-P2-049:
/// vitals, allergies, diagnoses, medicines, as the print view shows them), each section, the signature box (the signer's uploaded signature image,
/// when there is one), then each addendum as its own labelled row with its reason and signer; "Corrections are added as signed addenda" in the
/// footer of every page. Each medicine carries its prescription's number (MDV-P2-049 Q9).
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

    /// <summary>Renders the note; <paramref name="signer"/> is version 1's signer's profile and registration at this facility (null: none, the name alone prints).</summary>
    /// <exception cref="NotePdfUnsupportedTextException"/>
    public static byte[] Render(PatientSummary patient, FacilityInfo facility, NoteTemplate template, IReadOnlyList<Version> versions, PrintImages images, PrescriberInfo? signer)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
        var original = versions[0];
        string When(DateTimeOffset at) => Text("notePdf.when", ("date", TimeZoneInfo.ConvertTime(at, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)), ("zone", zone.Id));
        static PlainPdf.Row Row(string label, params (string, Tone)[] lines) => new(label, PlainPdf.Lines(lines));
        List<PlainPdf.Row> rows =
        [
            Row(Text("printFamily.label.signed"), (Text("notePdf.signedBy", ("version", "1"), ("name", original.SignerName), ("when", When(original.Row.SignedAt))), Tone.Strong),
                (Text("notePdf.hash", ("hash", original.Row.ContentHash)), Tone.Small)),
            .. Record(original.Row.AtSigning, zone),
        ];
        foreach (var section in template.Sections.Append(NoteTemplates.Notes))
            if (original.Row.Sections.TryGetValue(section.Id, out var text))
                rows.Add(Row(Text($"noteTemplates.section.{section.Id}"), [.. Lines(section, text)]));
        var signAfter = rows.Count - 1; // version 1 is signed here; each addendum carries its own signer
        foreach (var a in versions.Skip(1))
            rows.Add(Row(Text("printFamily.label.addendum"),
                (Text("notePdf.addendum", ("version", a.Row.VersionNo.ToString(CultureInfo.InvariantCulture)), ("name", a.SignerName), ("when", When(a.Row.SignedAt))), Tone.Strong),
                (Text("notePdf.reason", ("reason", a.Row.Reason ?? "")), Tone.Muted),
                (a.Row.Sections[SignedNoteVersion.AddendumSection], Tone.Body),
                (Text("notePdf.hash", ("hash", a.Row.ContentHash)), Tone.Small)));

        var signedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(original.Row.SignedAt, zone).DateTime);
        var mast = new PlainPdf.Masthead(facility.Name, facility.Address, facility.Phone, images.Logo, signer?.FullName ?? original.SignerName, signer is null ? []
            : [.. new[] { signer.Qualifications }.OfType<string>(), Text("printFamily.reg", ("number", signer.LicenceNumber), ("authority", signer.LicenceAuthority))]);
        var meta = new PlainPdf.Meta(Text("printFamily.noteTitle"), null, Text("printFamily.noteMeta", ("version", versions[^1].Row.VersionNo.ToString(CultureInfo.InvariantCulture))));
        var who = Row(Text("printFamily.label.patient"), (patient.FullName, Tone.Name), (Text("printFamily.patient", ("sex", Text($"rxPrint.sex.{patient.SexAtBirth}")),
            ("age", CountryRules.AgeOn(signedOn, patient.DateOfBirth).ToString(CultureInfo.InvariantCulture)), ("mrn", patient.Mrn),
            ("dob", patient.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))), Tone.Muted));
        var sign = new PlainPdf.Signature(images.Signature, signer?.FullName ?? original.SignerName,
            [.. signer is null ? Array.Empty<string>() : [Text("printFamily.regShort", ("number", signer.LicenceNumber))], Text("printFamily.signedAt", ("when", When(original.Row.SignedAt)))]);
        var footer = new PlainPdf.Footer(null, Text("printFamily.corrections"), null, null, null,
            (page, pages) => Text("printFamily.page", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        try
        {
            return PlainPdf.Write(PlainPdf.A4, mast, meta, who, rows, sign, footer, signAfter);
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
    static IEnumerable<PlainPdf.Row> Record(NoteAtSigning? r, TimeZoneInfo zone)
    {
        if (r is null) return [new(Text("notePrint.atSigning"), PlainPdf.Lines((Text("notePrint.notCaptured"), Tone.Body)))];
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
            ("status", Text($"problems.status.{d.Status}")), ("verification", Text($"problems.verification.{d.Verification}")), ("source", Text($"timeline.source.{d.Source}")));
        string Medicine(MedicineAtSigning m) => Text("notePrint.medicine", ("medicine", m.Dose is null ? m.Name : $"{m.Name} {m.Dose}"), ("number", RxNumber.Of(m.PrescriptionId)));
        static Action<QuestPDF.Infrastructure.IContainer> Body(IEnumerable<string> lines) => PlainPdf.Lines([.. lines.Select(x => (x, Tone.Body))]);
        return [
            new(Text("printFamily.label.vitals"), PlainPdf.Lines([
                (Text("notePrint.atSigning"), Tone.Small),
                .. (r.Vitals.Count == 0 ? [Text("notePrint.vitalsEmpty")] : r.Vitals.Select(Vital)).Select(x => (x, Tone.Body)),
                .. r.Vitals.Any(v => v.Code == ObservationCode.Weight) ? Array.Empty<(string, Tone)>() : [(Text("notePrint.weightNotRecorded"), Tone.Body)], // Q9: weight is always on the print, said when not taken
            ])),
            new(Text("patient360.allergies"), r.Allergies.Count > 0 ? PlainPdf.Allergy(Text("printFamily.allergy"), string.Join("; ", r.Allergies.Select(Allergy)))
                : Body([Text(r.AllergiesRecorded ? "banner.noAllergies" : "banner.allergiesNotRecorded")])),
            new(Text("printFamily.label.diagnoses"), Body(r.Diagnoses.Count == 0 ? [Text("problems.empty")] : r.Diagnoses.Select(Diagnosis))),
            new(Text("rxPrint.medicines"), Body(r.Medicines.Count == 0 ? [Text("notePrint.medicinesEmpty")] : r.Medicines.Select(Medicine))),
        ];
    }

    /// <summary>A text section as written; a structured one (MDV-P2-042) a line per answered item in words. An unanswered item is not printed.</summary>
    static IEnumerable<(string, Tone)> Lines(NoteSection section, string text) => section.Kind == SectionKind.Text ? [(text, Tone.Body)]
        : StructuredAnswers.Read(section, text).Select(a => (Text(a.Note is null ? "noteTemplates.answerLine" : "noteTemplates.answerLineNote", ("item", Text($"noteTemplates.item.{a.Item}")),
            ("answer", Text($"noteTemplates.answer.{a.Value}")), ("note", a.Note ?? "")), Tone.Body));

    static string Text(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
}
