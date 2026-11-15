using System.Globalization;
using Medvya.BuildingBlocks.Domain;

namespace Medvya.Notifications.Application;

/// <summary>
/// The wording of secure-link messages (MDV-P2-020): the clinic, the link or code, and its expiry. Never the patient's name, a medicine or any
/// clinical detail; "a document", not "your prescription", so a lock-screen preview says nothing about care. The registered SMS template keys are
/// "SecureLink.{lang}", "SecureCode.{lang}" and "SignInCode.{lang}" (SmsOptions.DltTemplateIds).
/// shortcut: Hindi and Arabic are working translations and the DLT registration of these templates is a person task with MDV-P1-045.
/// </summary>
public static class SecureLinkCatalog
{
    static readonly Dictionary<string, (string Subject, string Link, string Code)> Texts = new()
    {
        ["en"] = ("A document for you", "{0} has shared a document with you: {1} To open it, enter the code we text to your registered mobile. The link works until {2}.",
            "{0} is your code to open the document {1} shared. It expires in 10 minutes. Do not share it with anyone you did not send the document to."),
        ["hi"] = ("आपके लिए एक दस्तावेज़", "{0} ने आपके साथ एक दस्तावेज़ साझा किया है: {1} इसे खोलने के लिए, आपके पंजीकृत मोबाइल पर भेजा गया कोड डालें। यह लिंक {2} तक काम करेगा।",
            "{0} आपका कोड है, {1} द्वारा साझा किया गया दस्तावेज़ खोलने के लिए। यह 10 मिनट में समाप्त हो जाएगा। इसे केवल उसी को बताएँ जिसे आपने दस्तावेज़ भेजा है।"),
        ["ar"] = ("مستند لك", "شارك {0} مستندًا معك: {1} لفتحه، أدخل الرمز الذي نرسله إلى هاتفك المسجل. يعمل الرابط حتى {2}.",
            "{0} هو رمزك لفتح المستند الذي شاركه {1}. تنتهي صلاحيته خلال 10 دقائق. لا تشاركه إلا مع من أرسلت إليه المستند."),
    };

    static CultureInfo Culture(string lang) => MessageCatalog.Culture(lang);

    // The HTML email's own words (MDV-P1-111): the opening without the link (the button carries it), how to open it, the button and the expiry label.
    static readonly Dictionary<string, (string Lead, string How, string Open, string Expires)> EmailTexts = new()
    {
        ["en"] = ("{0} has shared a document with you.", "To open it, enter the code we text to your registered mobile.", "Open the document", "Expires"),
        ["hi"] = ("{0} ने आपके साथ एक दस्तावेज़ साझा किया है।", "इसे खोलने के लिए, आपके पंजीकृत मोबाइल पर भेजा गया कोड डालें।", "दस्तावेज़ खोलें", "समाप्ति"),
        ["ar"] = ("شارك {0} مستندًا معك.", "لفتحه، أدخل الرمز الذي نرسله إلى هاتفك المسجل.", "افتح المستند", "ينتهي"),
    };

    static string When(DateTimeOffset localExpiry, string lang) => localExpiry.ToString("d MMM yyyy h:mm tt", Culture(lang));

    public static RenderedMessage Link(string? languageTag, string clinic, string url, DateTimeOffset localExpiry)
    {
        var lang = MessageCatalog.Language(languageTag);
        var t = Texts[lang];
        return new(t.Subject, string.Format(Culture(lang), t.Link, clinic, url, When(localExpiry, lang)), $"SecureLink.{lang}");
    }

    public static RenderedMessage PharmacyLink(string clinic, string url, DateTimeOffset localExpiry) =>
        new("A shared document", $"{clinic} has shared a document with your pharmacy: {url} To open it, ask the patient for the code we text to their registered mobile. The link works until {When(localExpiry, "en")}.",
            "SecureLink.pharmacy");

    /// <summary>The HTML copy of <see cref="Link"/>: the button opens the link; the expiry is a labelled row with its time zone named.</summary>
    public static EmailView LinkEmail(string? languageTag, RenderedMessage text, string sender, string location, string url, DateTimeOffset localExpiry, string timeZone)
    {
        var lang = MessageCatalog.Language(languageTag);
        return View(lang, text, sender, location, string.Format(Culture(lang), EmailTexts[lang].Lead, sender), EmailTexts[lang].How, url, localExpiry, timeZone);
    }

    /// <summary>The HTML copy of <see cref="PharmacyLink"/>, in English like its text.</summary>
    public static EmailView PharmacyLinkEmail(RenderedMessage text, string sender, string location, string url, DateTimeOffset localExpiry, string timeZone) =>
        View("en", text, sender, location, $"{sender} has shared a document with your pharmacy.", "To open it, ask the patient for the code we text to their registered mobile.", url, localExpiry, timeZone);

    static EmailView View(string lang, RenderedMessage text, string sender, string location, string lead, string how, string url, DateTimeOffset localExpiry, string timeZone)
    {
        var t = EmailTexts[lang];
        return new(lang, sender, location == sender ? null : location, text.Subject, [lead],
            [new(t.Expires, When(localExpiry, lang), MessageCatalog.ZoneName(timeZone, lang))], new(t.Open, url), [how],
            [string.Format(Culture(lang), MessageCatalog.Words[lang].SentBy, sender)]);
    }

    public static RenderedMessage Code(string? languageTag, string code, string clinic)
    {
        var lang = MessageCatalog.Language(languageTag);
        return new("", string.Format(Culture(lang), Texts[lang].Code, code, clinic), $"SecureCode.{lang}");
    }

    static readonly Dictionary<string, string> SignInTexts = new()
    {
        ["en"] = "{0} is your Medvya sign-in code. It expires in 10 minutes. Never share it with anyone.",
        ["hi"] = "{0} आपका Medvya साइन-इन कोड है। यह 10 मिनट में समाप्त हो जाएगा। इसे किसी के साथ साझा न करें।",
        ["ar"] = "{0} هو رمز تسجيل الدخول إلى Medvya. تنتهي صلاحيته خلال 10 دقائق. لا تشاركه مع أي أحد.",
    };

    /// <summary>The patient app's sign-in code (MDV-P5-033): the code and its expiry only, no clinic and nothing about care.</summary>
    public static RenderedMessage SignInCode(string? languageTag, string code)
    {
        var lang = MessageCatalog.Language(languageTag);
        return new("", string.Format(Culture(lang), SignInTexts[lang], code), $"SignInCode.{lang}");
    }
}
