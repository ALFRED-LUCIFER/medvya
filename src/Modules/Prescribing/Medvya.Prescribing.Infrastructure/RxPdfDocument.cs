using System.Globalization;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Prescribing.Application;
using Medvya.Prescribing.Domain;
using Medvya.Tenancy.Application;

namespace Medvya.Prescribing.Infrastructure;

/// <summary>
/// The authorised prescription as an English PDF on A4 or A5 (MDV-P2-017, E-10) in the print family layout (MDV-P1-111): clinic with address, phone and registration number,
/// doctor with qualifications and registration, the Rx number and the patient's identifiers on every page; then labelled rows: the chart's allergies
/// (a listed allergy in the red block with a shield and the word), the diagnoses, each medicine numbered by its generic name in capitals with its
/// instruction and the notes its regulatory class needs boxed under it, the medicines to stop, advice and follow-up; the signature box with the
/// registration repeated, and the QR code that verifies it in the footer (its content hash is on that page, not on the paper).
/// Copy comes from the shared English catalogue (packages/i18n/messages/en.json); the server never invents wording.
/// Written by <see cref="PlainPdf"/>: names, medicines and notes in Hindi, Tamil or Arabic print shaped; text no embedded font has is refused.
/// shortcut: the PDF's instructions are English only (owner decision 11 Nov 2026: the pharmacist explains them); add a translator-reviewed
/// patient-language line if pilot clinics ask for it.
/// The clinic logo heads every page and the doctor's signature image sits in the signature box, when each was uploaded (Tenancy settings).
/// shortcut: brand names are not printed, because the composer does not choose one (generic only); print "generic (brand)" once it does.
/// </summary>
public static class RxPdfDocument
{
    /// <summary>The layout a doctor approves (exit gate 4): raise it whenever what the paper shows or where changes, so each doctor approves again.</summary>
    public const int LayoutVersion = 5; // 2: the logo and the signature image; 3: age, diagnosis, follow-up, a signature line, the hash on the verify page; 4: the print family layout;
    // 5: the practice's registration number in the masthead (#1365 F-068)

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Render(RxPrintView v, PlainPdf.Paper paper, PrintImages images)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(v.TimeZone);
        var when = TimeZoneInfo.ConvertTime(v.Authorisation.AuthorisedAt, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        var mast = new PlainPdf.Masthead(v.Clinic, v.ClinicAddress, v.ClinicPhone, images.Logo, v.Doctor,
            [.. new[] { v.Qualifications }.OfType<string>(), Text("printFamily.reg", ("number", v.LicenceNumber), ("authority", v.LicenceAuthority))],
            PaperText.Registration(v.ClinicRegistration));
        var meta = new PlainPdf.Meta(Text("rxPrint.title"), v.Authorisation.Number,
            Text("printFamily.rxMeta", ("version", v.Authorisation.VersionNo.ToString(CultureInfo.InvariantCulture)), ("when", when), ("zone", zone.Id)));
        var patient = new PlainPdf.Row(Text("printFamily.label.patient"), Patient(v.Patient.FullName, v.Patient.SexAtBirth, v.Patient.Mrn, v.Patient.DateOfBirth, v.PatientAge));

        List<PlainPdf.Row> rows =
        [
            new(Text("printFamily.label.allergy"), v.Allergies switch
            {
                ChartAllergies.Listed => PlainPdf.Allergy(Text("printFamily.allergy"), string.Join(", ", v.AllergySubstances)),
                ChartAllergies.NoKnownAllergies => PlainPdf.Lines((Text("banner.noAllergies"), PlainPdf.Tone.Body)),
                _ => PlainPdf.Lines((Text("banner.allergiesNotRecorded"), PlainPdf.Tone.Body)),
            }),
        ];
        if (v.Diagnoses is { Count: > 0 } diagnoses)
            rows.Add(new(Text("printFamily.label.diagnosis"), PlainPdf.Lines((string.Join(" · ",
                diagnoses.Select(d => d.Provisional ? Text("rxPrint.diagnosisProvisional", ("name", d.Description)) : d.Description)), PlainPdf.Tone.Body))));
        var medicines = v.Lines.Where(l => !l.Discontinue).Select(l => new PlainPdf.Item(l.Medicine.ToUpperInvariant(),
            string.Join(' ', new[] { l.Strength, l.Form }.Where(x => !string.IsNullOrEmpty(x))),
            [l.Sig["en"], .. l.Reason is { } why ? [Text("rxPrint.reason", ("reason", why))] : Array.Empty<string>()], // a change or taper from the reconciliation
            [.. l.PrintRequirements.Select(r => Text($"rxPrint.requirement.{r}"))])).ToList();
        if (medicines.Count > 0) rows.Add(new(Text("rxPrint.medicines"), PlainPdf.Items(medicines)));
        var stops = v.Lines.Where(l => l.Discontinue).ToList();
        if (stops.Count > 0)
            rows.Add(new(Text("printFamily.label.stop"), PlainPdf.Pairs(stops.Select(l => (l.Medicine.ToUpperInvariant(),
                l.Reason is { } reason ? $"· {Text("rxPrint.reason", ("reason", reason))}" : null)))));
        if (v.Advice is { Text: { } advice } a)
            rows.Add(new(Text("printFamily.label.advice"), a.InPatientLanguage ? PlainPdf.Lines((advice, PlainPdf.Tone.Body))
                : PlainPdf.Lines((advice, PlainPdf.Tone.Body), (Text("rxPrint.adviceInEnglish"), PlainPdf.Tone.Small))));
        if (v.FollowUpOn is { } due)
            rows.Add(new(Text("printFamily.label.followUp"), PlainPdf.Lines((Text("printFamily.followUpOn", ("date", due.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))), PlainPdf.Tone.Strong))));

        var sign = new PlainPdf.Signature(images.Signature, v.Doctor, [Text("printFamily.regShort", ("number", v.LicenceNumber)), Text("printFamily.authorisedAt", ("when", when))]);
        var footer = new PlainPdf.Footer(v.Qr, Text("printFamily.checkRx"), Text("printFamily.scanOrOpen", ("url", v.VerifyUrl)), null, v.Authorisation.Number,
            (page, pages) => Text("printFamily.page", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        return PlainPdf.Write(paper, mast, meta, patient, rows, sign, footer);
    }

    /// <summary>The patient row of every paper: the name, then sex, age, MRN and date of birth.</summary>
    internal static Action<QuestPDF.Infrastructure.IContainer> Patient(string name, string sex, string mrn, DateOnly dob, int age) => PlainPdf.Lines((name, PlainPdf.Tone.Name),
        (Text("printFamily.patient", ("sex", Text($"rxPrint.sex.{sex}")), ("age", age.ToString(CultureInfo.InvariantCulture)), ("mrn", mrn),
            ("dob", dob.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))), PlainPdf.Tone.Muted));

    static string Text(string key, params (string Name, string Value)[] values) => PaperText.Get(key, values);
}
