using System.Globalization;
using System.Text.Json;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Medvya.Billing.Infrastructure;

/// <summary>The printed papers' wording, from the shared English catalogue (packages/i18n/messages/en.json, embedded): the server never invents copy.</summary>
static class BillText
{
    static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(BillText).Assembly.GetManifestResourceStream("Medvya.Billing.messages.en.json")
            ?? throw new InvalidOperationException("The English message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string Get(string key, params (string Name, string Value)[] values) =>
        values.Aggregate(Catalog.Value.GetValueOrDefault(key) ?? throw new KeyNotFoundException($"Message key '{key}' is not in en.json."),
            (text, v) => text.Replace("{" + v.Name + "}", v.Value, StringComparison.Ordinal));

    /// <summary>An amount in minor units as the bill prints it: Indian digit grouping, two decimals, no symbol (1,23,456.50; -0.49).</summary>
    public static string Amount(long minor) =>
        (minor < 0 ? "-" : "") + (Math.Abs(minor) / 100).ToString("N0", India) + "." + (Math.Abs(minor) % 100).ToString("D2", CultureInfo.InvariantCulture);
}

/// <summary>A service line as printed: the service, its SAC and tax under it, quantity, rate and amount.</summary>
sealed record BillPaperLine(string Service, string Detail, string Quantity, string Rate, string Amount);

/// <summary>
/// What a printed bill or receipt says, already worded and formatted (BillingDocuments builds it): the clinic, the title and number, labelled facts,
/// the patient, the lines, the totals with the grand total, the amount in words, the payments (a receipt) and the closing note.
/// </summary>
sealed record BillPaper(
    string Clinic, string? Address, string? Phone, string? TaxLine, byte[]? Logo, string Title, string Number, string When,
    IReadOnlyList<(string Label, string Value)> Facts, string Patient, string Mrn, IReadOnlyList<BillPaperLine> Lines,
    IReadOnlyList<(string Label, string Amount)> Totals, string GrandLabel, string GrandAmount, string Words, IReadOnlyList<string> Payments, string Note);

/// <summary>
/// The bill and the receipt as PDFs (MDV-P4-005, design docs/02-design/print-family/billing.html): A5 in the print family layout (PlainPdf: masthead,
/// teal rule, hanging labels, the services table, "For the clinic · Authorised signatory"), or an 80 mm thermal roll in black only, as long as it needs.
/// shortcut: a draft format until the chartered accountant's review (MDV-P4-031, #488); the owner chose not to print "draft" on the paper (17 Nov 2026).
/// shortcut: English wording only, as the prescription; no logo on the thermal roll (one colour) and no QR verify code.
/// </summary>
static class BillPdf
{
    const float Mm = 2.8346f;
    const string Ink900 = "#142536", Ink600 = "#506579", Line = "#dfe7ed"; // packages/tokens on white paper, as PlainPdf

    static string T(string key, params (string, string)[] values) => BillText.Get(key, values);

    /// <exception cref="PdfUnsupportedTextException"/>
    public static byte[] Render(BillPaper p, DocumentLayout layout) => layout == DocumentLayout.Thermal ? Thermal(p) : A5(p);

    static byte[] A5(BillPaper p)
    {
        var mast = new PlainPdf.Masthead(p.Clinic, p.Address, p.Phone, p.Logo, "", [], p.TaxLine);
        List<PlainPdf.Row> rows = [.. p.Facts.Select(f => new PlainPdf.Row(f.Label, PlainPdf.Lines((f.Value, PlainPdf.Tone.Body))))];
        rows.Add(new(T("billing.print.services"), e => Services(e, p)));
        rows.Add(new(T("billing.print.inWords"), PlainPdf.Lines((p.Words, PlainPdf.Tone.Strong))));
        if (p.Payments.Count > 0) rows.Add(new(T("billing.print.paidBy"), PlainPdf.Lines([.. p.Payments.Select(x => (x, PlainPdf.Tone.Body))])));
        var patient = new PlainPdf.Row(T("billing.print.billedTo"), PlainPdf.Lines((p.Patient, PlainPdf.Tone.Name), (T("billing.print.mrn", ("mrn", p.Mrn)), PlainPdf.Tone.Muted)));
        var sign = new PlainPdf.Signature(null, T("billing.print.for", ("clinic", p.Clinic)), [T("billing.print.signatory")]);
        var footer = new PlainPdf.Footer(null, p.Note, null, null, p.Number,
            (page, pages) => T("printFamily.page", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        return PlainPdf.Write(PlainPdf.A5, mast, new PlainPdf.Meta(p.Title, p.Number, p.When), patient, rows, sign, footer);
    }

    /// <summary>The design's one real table: services with amounts right-aligned, then the totals with the grand total in the heaviest weight.</summary>
    static void Services(IContainer e, BillPaper p) => e.Column(c =>
    {
        c.Item().Table(t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.RelativeColumn();
                d.ConstantColumn(8 * Mm);
                d.ConstantColumn(17 * Mm);
                d.ConstantColumn(19 * Mm);
            });
            t.Header(h =>
            {
                foreach (var (key, end) in new[] { ("billing.print.col.service", false), ("billing.print.col.quantity", true), ("billing.print.col.rate", true), ("billing.print.col.amount", true) })
                    h.Cell().BorderBottom(0.7f).BorderColor(Ink900).PaddingBottom(3).Element(x => end ? x.AlignRight() : x)
                        .Text(T(key).ToUpperInvariant()).FontSize(6.8f).Bold().FontColor(Ink600).LetterSpacing(0.08f);
            });
            foreach (var l in p.Lines)
            {
                t.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).Column(s =>
                {
                    s.Item().Text(l.Service).Bold();
                    s.Item().Text(l.Detail).FontSize(7.4f).FontColor(Ink600);
                });
                foreach (var value in new[] { l.Quantity, l.Rate, l.Amount })
                    t.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).AlignRight().Text(value);
            }
        });
        c.Item().PaddingTop(3 * Mm).Row(r =>
        {
            r.RelativeItem();
            r.RelativeItem(1.1f).Column(totals =>
            {
                totals.Spacing(2.5f);
                foreach (var (label, amount) in p.Totals)
                    totals.Item().Row(x =>
                    {
                        x.RelativeItem().Text(label);
                        x.AutoItem().AlignRight().Text(amount);
                    });
                totals.Item().BorderTop(0.9f).BorderColor(Ink900).PaddingTop(4).Row(x =>
                {
                    x.RelativeItem().Text(p.GrandLabel).FontSize(12).ExtraBold();
                    x.AutoItem().AlignRight().Text(p.GrandAmount).FontSize(12).ExtraBold();
                });
            });
        });
    });

    /// <summary>The 80 mm roll: 72 mm of print width, black only, one page as long as the receipt.</summary>
    static byte[] Thermal(BillPaper p) => PlainPdf.Generate(Document.Create(d => d.Page(page =>
    {
        page.ContinuousSize(80, Unit.Millimetre);
        page.MarginHorizontal(4, Unit.Millimetre);
        page.MarginVertical(5, Unit.Millimetre);
        page.DefaultTextStyle(t => t.FontFamily([.. PlainPdf.FontFamilies]).FontSize(8.5f).LineHeight(1.3f).Medium().FontColor(Colors.Black));
        page.Content().Column(c =>
        {
            c.Item().Text(p.Clinic).FontSize(11).ExtraBold();
            foreach (var line in new[] { string.Join(" · ", new[] { p.Address, p.Phone }.Where(x => !string.IsNullOrWhiteSpace(x))), p.TaxLine })
                if (!string.IsNullOrEmpty(line)) c.Item().Text(line).FontSize(7);
            c.Item().PaddingVertical(3 * Mm).LineHorizontal(1.4f).LineColor(Colors.Black);
            c.Item().PaddingBottom(2 * Mm).Text(p.Title.ToUpperInvariant()).FontSize(10).ExtraBold().LetterSpacing(0.1f);
            foreach (var (label, value) in new[] { (T("billing.print.number"), p.Number), (T("billing.print.date"), p.When) }.Concat(p.Facts)
                         .Append((T("billing.print.patient"), $"{p.Patient} · {T("billing.print.mrn", ("mrn", p.Mrn))}")))
                c.Item().PaddingBottom(0.8f * Mm).Row(r =>
                {
                    r.ConstantItem(17 * Mm).Text(label).FontSize(8).Bold();
                    r.RelativeItem().Text(value).FontSize(8);
                });
            c.Item().PaddingVertical(2 * Mm).LineHorizontal(0.8f).LineColor(Colors.Black);
            foreach (var l in p.Lines)
                c.Item().PaddingVertical(1.2f * Mm).Row(r =>
                {
                    r.RelativeItem().Column(s =>
                    {
                        s.Item().Text(l.Quantity == "1" ? l.Service : $"{l.Service} × {l.Quantity}").FontSize(9);
                        s.Item().Text(l.Detail).FontSize(7).SemiBold();
                    });
                    r.AutoItem().PaddingLeft(3 * Mm).AlignRight().Text(l.Amount).FontSize(9).Bold();
                });
            c.Item().PaddingVertical(2 * Mm).LineHorizontal(0.8f).LineColor(Colors.Black);
            foreach (var (label, amount) in p.Totals)
                c.Item().Row(r =>
                {
                    r.RelativeItem().Text(label);
                    r.AutoItem().AlignRight().Text(amount);
                });
            c.Item().PaddingTop(2 * Mm).Row(r =>
            {
                r.RelativeItem().AlignBottom().Text(p.GrandLabel).FontSize(10).ExtraBold();
                r.AutoItem().AlignRight().Text(p.GrandAmount).FontSize(17).ExtraBold();
            });
            c.Item().PaddingTop(1.5f * Mm).Text(p.Words).FontSize(8);
            if (p.Payments.Count > 0)
                c.Item().PaddingTop(3 * Mm).Border(1.4f).BorderColor(Colors.Black).Padding(2 * Mm).Column(box =>
                {
                    box.Item().AlignCenter().Text(T("billing.print.paidBy").ToUpperInvariant()).FontSize(10).ExtraBold().LetterSpacing(0.06f);
                    foreach (var payment in p.Payments) box.Item().AlignCenter().Text(payment).FontSize(7.5f).SemiBold();
                });
            c.Item().PaddingTop(3 * Mm).Text(p.Note).FontSize(7);
        });
    })));
}
