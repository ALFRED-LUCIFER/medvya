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
/// The dental record and estimate as an A4 PDF (MDV-P8-011, MDV-P8-012) in the print family frame (MDV-P1-111): the clinic and the dentist printing it
/// in the masthead, the patient on every page, then labelled rows: the tooth chart (drawn as on screen, DentalChartPdf, then each tooth with a finding, in FDI order, condition in words, surfaces,
/// who and when), the treatment plan by phase with each item's tooth, state and estimate, the estimate total and the acceptance (who, how, when, or
/// "not accepted yet", and "changed since accepted" when the plan moved on). The chart rules are a draft until a dentist signs them; the page says so.
/// Copy comes from the shared English catalogue (packages/i18n/messages/en.json); the server never invents wording.
/// </summary>
public static class DentalPdfDocument
{
    static readonly CultureInfo India = new("en-IN");
    static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(DentalPdfDocument).Assembly.GetManifestResourceStream("Medvya.Encounters.messages.en.json")
            ?? throw new InvalidOperationException("The English message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    /// <exception cref="NotePdfUnsupportedTextException"/>
    public static byte[] Render(PatientSummary patient, FacilityInfo facility, DentalChartView chart, IReadOnlyDictionary<Guid, string> names, PrintImages images,
        PrescriberInfo? dentist, string printedBy, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
        string When(DateTimeOffset at) => Text("notePdf.when", ("date", TimeZoneInfo.ConvertTime(at, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture)), ("zone", zone.Id));
        static PlainPdf.Row Row(string label, params (string, Tone)[] lines) => new(label, PlainPdf.Lines(lines));

        string Finding(ToothFindingView f) => Text(f.Surfaces is null ? "dentalPdf.finding" : "dentalPdf.findingSurfaces", ("tooth", f.Tooth),
            ("condition", Text($"dental.condition.{f.Condition}")), ("surfaces", f.Surfaces ?? ""),
            ("who", names.GetValueOrDefault(f.RecordedBy) ?? ""), ("when", When(f.RecordedAt))) + (f.Note is { } n ? $" · {n}" : "");
        string Item(DentalPlanItemView i) => Text("dentalPdf.planItem", ("service", i.ServiceName),
            ("tooth", i.Tooth is null ? Text("dental.plan.wholeMouth") : i.Surfaces is null ? i.Tooth : $"{i.Tooth} {i.Surfaces}"),
            ("status", Text($"dental.plan.status.{i.Status}")), ("price", Money(i.EstimateMinor, i.Currency)))
            + (i.Status == DentalPlanStatus.Declined && i.Reason is { } r ? " · " + Text("dentalPdf.reason", ("reason", r)) : "");

        var teeth = chart.Teeth.OrderBy(t => t.Tooth, StringComparer.Ordinal).ToList();
        var plan = chart.Plan;
        var check = chart.HistoryCheck;
        List<PlainPdf.Row> rows =
        [
            // MDV-P8-014: the medical history check, with each answer that raises a risk in words.
            Row(Text("dentalPdf.label.history"), check is null ? [(Text("dentalPdf.historyNever"), Tone.Body)] :
            [
                (Text("dentalPdf.historyConfirmed", ("when", When(check.ConfirmedAt)), ("who", names.GetValueOrDefault(check.ConfirmedBy) ?? "")), Tone.Body),
                (check.Yes.Count == 0 ? Text("dentalPdf.historyNone")
                    : Text("dentalPdf.historyYes", ("list", string.Join(", ", check.Yes.Select(q => Text($"dental.medical.question.{q}"))))), check.Yes.Count == 0 ? Tone.Body : Tone.Strong),
                .. check.Note is { } n ? [(n, Tone.Muted)] : Array.Empty<(string, Tone)>(),
            ]),
            new(Text("dentalPdf.label.chart"), DentalChartPdf.Part(teeth, key => Text(key)), PlainPdf.Lines([.. teeth.Count == 0 ? [(Text("dentalPdf.noFindings"), Tone.Body)]
                : teeth.Select(t => (Finding(t), Tone.Body)), (Text(DentalRules.SignOff is null ? "dental.rulesDraft" : "dental.rulesSigned"), Tone.Small)])),
        ];
        foreach (var phase in plan.Items.GroupBy(i => i.Phase).OrderBy(g => g.Key))
            rows.Add(Row(Text("dentalPdf.label.phase", ("phase", phase.Key.ToString(CultureInfo.InvariantCulture))), [.. phase.Select(i => (Item(i), Tone.Body))]));
        if (plan.Items.Count == 0) rows.Add(Row(Text("dentalPdf.label.plan"), (Text("dentalPdf.noPlan"), Tone.Body)));
        else rows.Add(Row(Text("dentalPdf.label.estimate"),
            (Text("dentalPdf.total", ("total", Money(plan.TotalMinor, plan.Currency ?? "INR"))), Tone.Strong),
            (Text("dentalPdf.estimateNote"), Tone.Muted),
            plan.Acceptance is { } a
                ? (Text("dentalPdf.accepted", ("name", a.AcceptedBy), ("method", Text($"dental.acceptance.method.{a.Method}")), ("when", When(a.AcceptedAt)),
                    ("total", Money(a.TotalMinor, a.Currency))) + (plan.AcceptanceCurrent ? "" : " · " + Text("dental.acceptance.changed")), Tone.Body)
                : (Text("dentalPdf.notAccepted"), Tone.Body)));

        var printedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var mast = new PlainPdf.Masthead(facility.Name, facility.Address, facility.Phone, images.Logo, dentist?.FullName ?? printedBy, dentist is null ? []
            : [.. new[] { dentist.Qualifications }.OfType<string>(), Text("printFamily.reg", ("number", dentist.LicenceNumber), ("authority", dentist.LicenceAuthority))],
            string.IsNullOrWhiteSpace(facility.Registration) ? null : Text("rxPrint.registration", ("number", facility.Registration)));
        var meta = new PlainPdf.Meta(Text("dentalPdf.title"), null, Text("dentalPdf.meta", ("when", When(now))));
        var who = Row(Text("printFamily.label.patient"), (patient.FullName, Tone.Name), (Text("printFamily.patient", ("sex", Text($"rxPrint.sex.{patient.SexAtBirth}")),
            ("age", CountryRules.AgeOn(printedOn, patient.DateOfBirth).ToString(CultureInfo.InvariantCulture)), ("mrn", patient.Mrn),
            ("dob", patient.DateOfBirth.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))), Tone.Muted));
        var sign = new PlainPdf.Signature(images.Signature, dentist?.FullName ?? printedBy,
            [.. dentist is null ? Array.Empty<string>() : [Text("printFamily.regShort", ("number", dentist.LicenceNumber))], Text("dentalPdf.printedAt", ("when", When(now)))]);
        var footer = new PlainPdf.Footer(null, Text("dentalPdf.footer"), null, null, null,
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

    /// <summary>₹1,23,456.00 (Indian grouping) or AED 1,234.00, from integers, never a float. Same rule as Billing's papers (module boundary: a copy, not a reference).</summary>
    internal static string Money(long minor, string currency)
    {
        var abs = Math.Abs(minor);
        var number = string.Create(currency == "INR" ? India : CultureInfo.InvariantCulture, $"{abs / 100:N0}.{abs % 100:D2}");
        return (minor < 0 ? "−" : "") + (currency == "INR" ? "₹" + number : $"{currency} {number}");
    }

    static string Text(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));
}
