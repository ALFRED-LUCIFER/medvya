using System.Globalization;
using System.Text.Json;
using Medvya.BuildingBlocks.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Encounters.Application;
using Medvya.Encounters.Domain;
using Medvya.Patients.Application;
using Medvya.Tenancy.Application;
using QuestPDF.Fluent;
using Tone = Medvya.BuildingBlocks.Infrastructure.PlainPdf.Tone;

namespace Medvya.Encounters.Infrastructure;

/// <summary>
/// The signed procedure consent (MDV-P8-019) as an A4 PDF in the print family frame, in the language the patient read it in (English, Hindi or Arabic): the
/// clinic, the patient, the procedure and tooth, the consent wording of its template version, the patient's declaration, the drawn signature with the
/// signer's name and the time, and the dentist who took it. A draft wording says so on the form until a lawyer and a dentist approve it. Copy comes from
/// the shared catalogues (packages/i18n/messages); the server never invents wording.
/// shortcut: Arabic text is set in its own script inside the left-to-right frame; mirror the frame when a clinic in the UAE asks for it.
/// </summary>
public static class DentalConsentPdfDocument
{
    static readonly Dictionary<string, Lazy<Dictionary<string, string>>> Catalogs = DentalConsentRules.Languages.ToDictionary(l => l, l => new Lazy<Dictionary<string, string>>(() =>
    {
        using var stream = typeof(DentalConsentPdfDocument).Assembly.GetManifestResourceStream($"Medvya.Encounters.messages.{l}.json")
            ?? throw new InvalidOperationException($"The {l} message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }));

    /// <exception cref="NotePdfUnsupportedTextException"/>
    public static byte[] Render(PatientSummary patient, FacilityInfo facility, DentalPlanItem item, string key, int version, string language, string signedBy, byte[] signature,
        string dentist, DateTimeOffset at)
    {
        string Text(string k, params (string Name, string Value)[] values) =>
            values.Aggregate(Catalogs[language].Value.GetValueOrDefault(k) ?? Catalogs["en"].Value.GetValueOrDefault(k) ?? throw new KeyNotFoundException($"Message key '{k}' is missing."),
                (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
        var when = Text("notePdf.when", ("date", TimeZoneInfo.ConvertTime(at, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)), ("zone", zone.Id));
        static PlainPdf.Row Row(string label, params (string, Tone)[] lines) => new(label, PlainPdf.Lines(lines));

        var printedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var procedure = item.Tooth is null ? item.ServiceName : $"{item.ServiceName} · {Text("dentalConsent.tooth", ("tooth", item.Tooth))}";
        List<PlainPdf.Row> rows =
        [
            Row(Text("dentalConsent.label.procedure"), (procedure, Tone.Strong)),
            Row(Text("dentalConsent.label.consent"), [.. Text($"dentalConsent.{key}.body").Split("\n\n").Select(p => (p, Tone.Body)),
                .. DentalConsentRules.Approved is null ? [(Text("dentalConsent.draft"), Tone.Small)] : Array.Empty<(string, Tone)>()]),
            Row(Text("dentalConsent.label.declaration"), (Text("dentalConsent.declaration"), Tone.Body)),
            new(Text("dentalConsent.label.signed"),
                e => e.Height(51).AlignLeft().Image(signature).FitHeight(), // 18 mm
                PlainPdf.Lines((signedBy, Tone.Strong), (when, Tone.Muted))),
        ];
        var mast = new PlainPdf.Masthead(facility.Name, facility.Address, facility.Phone, null, dentist, [], null);
        var meta = new PlainPdf.Meta(Text($"dentalConsent.{key}.title"), null, Text("dentalConsent.meta", ("version", version.ToString(CultureInfo.InvariantCulture)), ("when", when)));
        var who = Row(Text("printFamily.label.patient"), (patient.FullName, Tone.Name), (Text("printFamily.patient", ("sex", Text($"rxPrint.sex.{patient.SexAtBirth}")),
            ("age", CountryRules.AgeOn(printedOn, patient.DateOfBirth).ToString(CultureInfo.InvariantCulture)), ("mrn", patient.Mrn),
            ("dob", patient.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))), Tone.Muted));
        var sign = new PlainPdf.Signature(null, dentist, [Text("dentalConsent.takenBy", ("when", when))]);
        var footer = new PlainPdf.Footer(null, Text("dentalConsent.footer"), null, null, null,
            (page, pages) => Text("printFamily.page", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        try
        {
            return PlainPdf.Write(PlainPdf.A4, mast, meta, who, rows, sign, footer);
        }
        catch (PdfUnsupportedTextException)
        {
            throw new NotePdfUnsupportedTextException();
        }
    }
}
