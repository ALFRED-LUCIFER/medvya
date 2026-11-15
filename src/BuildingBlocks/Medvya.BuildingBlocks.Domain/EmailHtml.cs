using System.Globalization;
using System.Net;
using System.Text;

namespace Medvya.BuildingBlocks.Domain;

/// <summary>A labelled row: the label hangs in the start margin; <paramref name="Ltr"/> follows the value in a left-to-right run (a phone number in Arabic).</summary>
public sealed record EmailFact(string Label, string Value, string? Detail = null, string? Ltr = null);

/// <summary>The one teal button, only when there is something to do.</summary>
public sealed record EmailButton(string Label, string Url);

/// <summary>What one branded email shows (MDV-P1-111, docs/02-design/print-family "Emails"). Every value is text; the layout escapes it.</summary>
/// <param name="Language">"en", "hi" or "ar" (Arabic reads right to left).</param>
/// <param name="Sender">The clinic's name, or "Medvya" for Medvya's own mail: the masthead and the logo's alt text.</param>
/// <param name="SenderLine">The small line under it: the location, or "on Medvya"; null leaves it out.</param>
/// <param name="Lead">The opening sentence(s), 16 px.</param>
/// <param name="After">Small print after the facts and button, 14 px.</param>
/// <param name="Footer">Who sent it and why.</param>
public sealed record EmailView(string Language, string Sender, string? SenderLine, string Title, IReadOnlyList<string> Lead,
    IReadOnlyList<EmailFact> Facts, EmailButton? Button, IReadOnlyList<string> After, IReadOnlyList<string> Footer);

/// <summary>
/// The shared HTML layout of every email: 600 px, tables and inline styles only, because Outlook and Gmail ignore modern CSS.
/// Masthead (logo, name, small line, teal rule), title, opening, hanging-label facts, an optional button with its link spelt out, footer band.
/// </summary>
public static class EmailHtml
{
    /// <summary>The Content-ID of the inline logo image.</summary>
    public const string LogoId = "logo";

    /// <summary>The footer of Medvya's own mail to staff and organisations (English, like those texts).</summary>
    public const string MedvyaFooter = "Medvya · clinic software for India and the UAE.";

    const string Ink = "#142536", Muted = "#506579", Secondary = "#334e68", Line = "#dfe7ed", Brand = "#0f766e", BrandInk = "#102a43", Band = "#f7fafc";

    static readonly Dictionary<string, string> Fallback = new()
    {
        ["en"] = "If the button does not work, open this link:",
        ["hi"] = "अगर बटन काम न करे, तो यह लिंक खोलें:",
        ["ar"] = "إذا لم يعمل الزر، افتح هذا الرابط:",
    };

    static string E(string text) => WebUtility.HtmlEncode(text);

    /// <param name="logo">True when the message carries the clinic logo as <c>cid:logo</c>.</param>
    public static string Render(EmailView v, bool logo)
    {
        var lang = Fallback.ContainsKey(v.Language) ? v.Language : "en";
        var rtl = lang == "ar";
        var dir = rtl ? "rtl" : "ltr";
        var font = rtl ? "'Noto Sans Arabic',Manrope,system-ui,sans-serif" : "Manrope,'Noto Sans Arabic',system-ui,sans-serif";
        var start = rtl ? "right" : "left";
        // Upper case and letter spacing are for Latin labels only: spacing breaks Arabic joining and Devanagari shaping, so those labels are a size larger instead.
        var label = lang == "en" ? "font-size:11px;letter-spacing:0.08em;text-transform:uppercase;" : "font-size:12px;";
        var h = new StringBuilder();
        h.Append(CultureInfo.InvariantCulture, $"""<!doctype html><html lang="{lang}" dir="{dir}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light"><title>{E(v.Title)}</title></head>""");
        h.Append(CultureInfo.InvariantCulture, $"""<body style="margin:0;padding:0;background:{Band};"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{Band};"><tr><td align="center" style="padding:24px 8px;">""");
        h.Append(CultureInfo.InvariantCulture, $"""<table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" dir="{dir}" style="width:100%;max-width:600px;background:#ffffff;border:1px solid {Line};border-radius:12px;font-family:{font};color:{Ink};text-align:{start};">""");

        // Masthead.
        h.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:24px 28px 18px;border-bottom:2px solid {Brand};"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>""");
        if (logo) h.Append(CultureInfo.InvariantCulture, $"""<td valign="middle" width="48" style="width:48px;"><img src="cid:{LogoId}" width="36" height="36" alt="{E(v.Sender)}" style="display:block;width:36px;height:36px;border:0;"></td>""");
        h.Append(CultureInfo.InvariantCulture, $"""<td valign="middle"><div style="font-size:17px;line-height:22px;font-weight:800;color:{BrandInk};">{E(v.Sender)}</div>""");
        if (v.SenderLine is { } line) h.Append(CultureInfo.InvariantCulture, $"""<div style="font-size:12px;line-height:16px;color:{Muted};">{E(line)}</div>""");
        h.Append("</td></tr></table></td></tr>");

        // Body.
        h.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:26px 28px 24px;"><h2 style="margin:0 0 8px;font-size:22px;line-height:28px;font-weight:800;letter-spacing:-0.01em;color:{Ink};">{E(v.Title)}</h2>""");
        foreach (var p in v.Lead) h.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 16px;font-size:16px;line-height:24px;">{E(p)}</p>""");
        if (v.Facts.Count > 0)
        {
            h.Append("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:6px 0 22px;">""");
            for (var i = 0; i < v.Facts.Count; i++)
            {
                var f = v.Facts[i];
                var rule = i == 0 ? "" : $"border-top:1px solid {Line};";
                h.Append(CultureInfo.InvariantCulture, $"""<tr><td valign="top" width="96" style="width:96px;padding:14px 0 11px;{rule}{label}line-height:16px;font-weight:700;color:{Muted};">{E(f.Label)}</td>""");
                h.Append(CultureInfo.InvariantCulture, $"""<td valign="top" style="padding:11px 0;{rule}font-size:16px;line-height:24px;font-weight:700;">{E(f.Value)}""");
                if (f.Ltr is { } ltr) h.Append(CultureInfo.InvariantCulture, $"""<span dir="ltr">{E(ltr)}</span>""");
                if (f.Detail is { } d) h.Append(CultureInfo.InvariantCulture, $"""<div style="font-size:14px;line-height:20px;font-weight:500;color:{Secondary};">{E(d)}</div>""");
                h.Append("</td></tr>");
            }
            h.Append("</table>");
        }
        if (v.Button is { } b)
        {
            // 12 + 20 + 12 = 44 px tall: padding, not min-height, which Outlook ignores.
            h.Append(CultureInfo.InvariantCulture, $"""<table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td style="border-radius:10px;background:{Brand};"><a href="{E(b.Url)}" style="display:inline-block;padding:12px 22px;font-size:15px;line-height:20px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:10px;">{E(b.Label)}</a></td></tr></table>""");
            h.Append(CultureInfo.InvariantCulture, $"""<p style="margin:14px 0 16px;font-size:13px;line-height:18px;color:{Muted};">{E(Fallback[lang])} <a href="{E(b.Url)}" dir="ltr" style="color:{Ink};word-break:break-all;">{E(b.Url)}</a></p>""");
        }
        foreach (var p in v.After) h.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 16px;font-size:14px;line-height:20px;color:{Secondary};">{E(p)}</p>""");
        h.Append("</td></tr>");

        // Footer band.
        h.Append(CultureInfo.InvariantCulture, $"""<tr><td style="padding:18px 28px 24px;background:{Band};border-top:1px solid {Line};border-radius:0 0 12px 12px;font-size:12px;line-height:18px;color:{Muted};">""");
        for (var i = 0; i < v.Footer.Count; i++) h.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 {(i == v.Footer.Count - 1 ? 0 : 6)}px;font-size:12px;line-height:18px;">{E(v.Footer[i])}</p>""");
        h.Append("</td></tr></table></td></tr></table></body></html>");
        return h.ToString();
    }
}
