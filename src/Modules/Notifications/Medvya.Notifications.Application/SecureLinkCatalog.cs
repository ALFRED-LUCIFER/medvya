using System.Globalization;

namespace Medvya.Notifications.Application;

/// <summary>
/// The wording of secure-link messages (MDV-P2-020): the clinic, the link or code, and its expiry. Never the patient's name, a medicine or any
/// clinical detail; "a document", not "your prescription", so a lock-screen preview says nothing about care. The registered SMS template keys are
/// "SecureLink.{lang}" and "SecureCode.{lang}" (SmsOptions.DltTemplateIds).
/// shortcut: Hindi and Arabic are working translations and the DLT registration of these two templates is a person task with MDV-P1-045.
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

    static CultureInfo Culture(string lang) => CultureInfo.GetCultureInfo(lang switch { "hi" => "hi-IN", "ar" => "ar-AE", _ => "en-IN" });

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

    public static RenderedMessage Code(string? languageTag, string code, string clinic)
    {
        var lang = MessageCatalog.Language(languageTag);
        return new("", string.Format(Culture(lang), Texts[lang].Code, code, clinic), $"SecureCode.{lang}");
    }
}
