using System.Globalization;
using System.Text.Json;
using Medvya.Billing.Application;
using Medvya.Billing.Domain;
using Medvya.BuildingBlocks.Infrastructure;
using Medvya.Tenancy.Application;
using Tone = Medvya.BuildingBlocks.Infrastructure.PlainPdf.Tone;

namespace Medvya.Billing.Infrastructure;

/// <summary>
/// The shift report of a closed cash session (P-13, MDV-P4-012) as an A4 PDF in the print family frame: the clinic and location, who opened, counted and
/// confirmed and when (facility time), the takings by method, the counted notes and coins, expected cash, the variance and its note, and any later
/// adjustments. Counts and amounts only, never a patient. English copy from the shared catalogue (packages/i18n/messages/en.json).
/// </summary>
public static class ShiftReportPdfDocument
{
    static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(ShiftReportPdfDocument).Assembly.GetManifestResourceStream("Medvya.Billing.messages.en.json")
            ?? throw new InvalidOperationException("The English message catalogue is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static byte[] Render(CashSessionView s, FacilityInfo facility, byte[]? logo)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(facility);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZone);
        string When(DateTimeOffset? at) => at is { } x ? TimeZoneInfo.ConvertTime(x, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) : "";
        string Amount(long minor) => Money(minor, s.Currency);
        string Signed(long minor) => (minor > 0 ? "+" : "") + Amount(minor);
        static PlainPdf.Row Row(string label, params (string, Tone)[] lines) => new(label, PlainPdf.Lines(lines));

        List<PlainPdf.Row> rows =
        [
            Row(Text("billing.dayClose.pdf.byMethod"), [.. s.Totals.Select(t => (Text("billing.dayClose.pdf.methodLine", ("method", Text($"billing.method.{t.Method}")),
                ("count", t.Payments.ToString(CultureInfo.InvariantCulture)), ("amount", Amount(t.AmountMinor))), Tone.Body)),
                .. s.Totals.Where(t => t.Refunds > 0).Select(t => (Text("billing.dayClose.pdf.refundLine", ("method", Text($"billing.method.{t.Method}")),
                    ("count", t.Refunds.ToString(CultureInfo.InvariantCulture)), ("amount", Amount(-t.RefundsMinor))), Tone.Body)),
                .. s.Totals.Count == 0 ? [(Text("billing.dayClose.noPayments"), Tone.Muted)] : Array.Empty<(string, Tone)>()]),
            Row(Text("billing.dayClose.pdf.denominations"), [.. s.Counts.Where(c => c.Count > 0).Select(c => (Text("billing.dayClose.pdf.denominationLine",
                ("value", Amount(c.ValueMinor)), ("count", c.Count.ToString(CultureInfo.InvariantCulture)), ("amount", Amount(c.ValueMinor * c.Count))), Tone.Body)),
                (Text("billing.dayClose.counted") + " " + Amount(s.CountedMinor ?? 0), Tone.Strong)]),
            Row(Text("billing.dayClose.pdf.cash"),
                (Text("billing.dayClose.float") + " " + Amount(s.OpeningFloatMinor), Tone.Body),
                (Text("billing.dayClose.cashReceipts") + " " + Amount(s.Totals.Where(t => t.Method == PaymentMethod.Cash).Sum(t => t.AmountMinor)), Tone.Body),
                (Text("billing.dayClose.cashRefunds") + " " + Amount(-s.Totals.Where(t => t.Method == PaymentMethod.Cash).Sum(t => t.RefundsMinor)), Tone.Body),
                (Text("billing.dayClose.expected") + " " + Amount(s.ExpectedMinor), Tone.Strong),
                (Text("billing.dayClose.variance") + " " + Signed(s.VarianceMinor ?? 0), Tone.Strong)),
        ];
        if (s.Note is { } note) rows.Add(Row(Text("billing.dayClose.note"), (note, Tone.Body)));
        if (s.Adjustments.Count > 0)
            rows.Add(Row(Text("billing.dayClose.adjustments"), [.. s.Adjustments.Select(a => (Text("billing.dayClose.pdf.adjustmentLine",
                ("amount", Signed(a.AmountMinor)), ("reason", a.Reason), ("name", a.CreatedByName), ("when", When(a.CreatedAt))), Tone.Body))]));

        var mast = new PlainPdf.Masthead(facility.TenantName, facility.Address, facility.Phone, logo, facility.Name, [],
            string.IsNullOrWhiteSpace(facility.Registration) ? null : Text("rxPrint.registration", ("number", facility.Registration)));
        var meta = new PlainPdf.Meta(Text("billing.dayClose.pdf.title"), null, Text("billing.dayClose.pdf.zone", ("zone", zone.Id)));
        var who = Row(Text("billing.dayClose.pdf.shift"),
            (Text("billing.dayClose.pdf.opened", ("name", s.OpenedByName), ("when", When(s.OpenedAt))), Tone.Strong),
            (Text("billing.dayClose.pdf.countedBy", ("name", s.CountedByName ?? ""), ("when", When(s.CountedAt))), Tone.Muted),
            (s.ConfirmedByName is { } by ? Text("billing.dayClose.pdf.confirmedBy", ("name", by), ("when", When(s.ClosedAt))) : Text("billing.dayClose.pdf.noConfirmation"), Tone.Muted));
        var sign = new PlainPdf.Signature(null, s.ConfirmedByName ?? s.CountedByName ?? "", [Text("billing.dayClose.pdf.closedAt", ("when", When(s.ClosedAt)))]);
        var footer = new PlainPdf.Footer(null, Text("billing.dayClose.pdf.footer"), null, null, null,
            (page, pages) => Text("printFamily.page", ("page", page.ToString(CultureInfo.InvariantCulture)), ("pages", pages.ToString(CultureInfo.InvariantCulture))));
        return PlainPdf.Write(PlainPdf.A4, mast, meta, who, rows, sign, footer);
    }

    /// <summary>₹1,23,456.00 (Indian grouping) or AED 1,234.00; whole units and the minor part from integers, never a float.</summary>
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
