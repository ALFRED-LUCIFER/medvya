using System.Globalization;
using System.Text.Json;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The authorised prescription as an English PDF on A4 or A5 (MDV-P2-017, E-10): clinic, doctor with qualifications and licence on every page with
/// the patient's identifiers and the Rx number, then the chart's allergies, each medicine by its generic name with its instruction and the notes its
/// regulatory class needs, the medicines to stop, the authorisation with its content hash, and the QR code that verifies it.
/// Copy comes from the shared English catalogue (packages/i18n/messages/en.json); the server never invents wording.
/// Written by <see cref="PlainPdf"/>: names, medicines and notes in Hindi, Tamil or Arabic print shaped; text no embedded font has is refused.
/// shortcut: the PDF's instructions are English only (owner decision 11 Nov 2026: the pharmacist explains them); add a translator-reviewed
/// patient-language line if pilot clinics ask for it.
/// shortcut: no clinic logo or signature image yet; both come with the stored-copy-in-Blob change (MDV-P2-017).
/// shortcut: brand names are not printed, because the composer does not choose one (generic only); print "generic (brand)" once it does.
/// </summary>
public static class RxPdfDocument
{
    /// <summary>The layout a doctor approves (exit gate 4): raise it whenever what the paper shows or where changes, so each doctor approves again.</summary>
    public const int LayoutVersion = 1;

    static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(RxPdfDocument).Assembly.GetManifestResourceStream("Medvya.Prescribing.messages.en.json")
            ?? throw new InvalidOperationException("The English message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Render(RxPrintView v, PlainPdf.Paper paper)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(v.TimeZone);
        var when = TimeZoneInfo.ConvertTime(v.Authorisation.AuthorisedAt, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        string[] header =
        [
            v.Clinic,
            v.Qualifications is null ? v.Doctor : Text("rxPrint.doctor", ("name", v.Doctor), ("qualifications", v.Qualifications)),
            Text("rxPrint.licence", ("number", v.LicenceNumber), ("authority", v.LicenceAuthority)),
            Text("rxPrint.patient", ("name", v.Patient.FullName), ("sex", Text($"rxPrint.sex.{v.Patient.SexAtBirth}")), ("mrn", v.Patient.Mrn),
                ("dob", v.Patient.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))),
            Text("rxPrint.number", ("number", v.Authorisation.Number), ("version", v.Authorisation.VersionNo.ToString(CultureInfo.InvariantCulture)), ("when", when), ("zone", zone.Id)),
        ];
        List<(string Text, bool Bold)> body =
        [
            (v.Allergies switch
            {
                ChartAllergies.Listed => Text("rxPrint.allergies", ("list", string.Join(", ", v.AllergySubstances))),
                ChartAllergies.NoKnownAllergies => Text("rxPrint.allergiesNone"),
                _ => Text("rxPrint.allergiesNotRecorded"),
            }, true),
            ("", false),
        ];
        var n = 0;
        foreach (var l in v.Lines.Where(l => !l.Discontinue))
        {
            body.Add(($"{++n}. {string.Join(' ', new[] { l.Medicine, l.Strength, l.Form }.Where(x => !string.IsNullOrEmpty(x)))}", true));
            body.Add((l.Sig["en"], false));
            body.AddRange(l.PrintRequirements.Select(r => (Text($"rxPrint.requirement.{r}"), false)));
            body.Add(("", false));
        }
        foreach (var l in v.Lines.Where(l => l.Discontinue))
        {
            body.Add((Text("rxPrint.stop", ("medicine", l.Medicine)), true));
            if (l.Reason is { } reason) body.Add((Text("rxPrint.reason", ("reason", reason)), false));
            body.Add(("", false));
        }
        body.AddRange([
            (Text("rxPrint.authorised", ("name", v.Doctor), ("when", when)), true),
            (Text("rxPrint.hash", ("hash", v.Authorisation.ContentHash)), false),
            (Text("rxPrint.verify", ("url", v.VerifyUrl)), false),
        ]);
        return PlainPdf.Write(header, body, (page, pages) => Text("rxPrint.footer", ("number", v.Authorisation.Number),
            ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))), paper, v.Qr);
    }

    static string Text(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
}
