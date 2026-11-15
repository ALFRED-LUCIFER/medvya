using System.Globalization;
using System.Text.RegularExpressions;
using Medvya.BuildingBlocks.Domain;
using Medvya.Notifications.Domain;

namespace Medvya.Notifications.Application;

/// <summary>A message ready to send. <see cref="TemplateKey"/> names the registered SMS template ("Confirmation.en").</summary>
public sealed record RenderedMessage(string Subject, string Body, string TemplateKey);

/// <summary>
/// The wording of every patient message, in en / hi / ar. Texts name the clinic, the date and the time and nothing else: no
/// doctor, reason, visit type or any clinical detail (MDV-P1-032/033). Slots are written <c>{#var#}</c>, the India DLT way, so
/// the same text is the email body and the SMS template that is registered with the operator. Both reminders share one template.
/// shortcut: Hindi and Arabic wording is a working translation; the DLT-registered final text comes with MDV-P1-045 and the legal/language review.
/// </summary>
public static partial class MessageCatalog
{
    public const string Slot = "{#var#}";
    public const int MaxSlotLength = 30; // DLT: a variable is at most 30 characters
    static readonly string[] Languages = ["en", "hi", "ar"];

    // (subject, text) by template group and language; slot order is always clinic, date, time.
    static readonly Dictionary<string, Dictionary<string, (string Subject, string Text)>> Texts = new()
    {
        ["Confirmation"] = new()
        {
            ["en"] = ("Appointment confirmed", "Your appointment at {#var#} is confirmed for {#var#} at {#var#}."),
            ["hi"] = ("अपॉइंटमेंट की पुष्टि", "{#var#} में आपकी अपॉइंटमेंट {#var#} को {#var#} बजे के लिए पक्की है।"),
            ["ar"] = ("تأكيد الموعد", "تم تأكيد موعدك في {#var#} بتاريخ {#var#} الساعة {#var#}."),
        },
        ["Rescheduled"] = new()
        {
            ["en"] = ("Appointment moved", "Your appointment at {#var#} has been moved to {#var#} at {#var#}."),
            ["hi"] = ("अपॉइंटमेंट बदली गई", "{#var#} में आपकी अपॉइंटमेंट अब {#var#} को {#var#} बजे है।"),
            ["ar"] = ("تم نقل الموعد", "تم نقل موعدك في {#var#} إلى {#var#} الساعة {#var#}."),
        },
        ["Cancelled"] = new()
        {
            ["en"] = ("Appointment cancelled", "Your appointment at {#var#} on {#var#} at {#var#} has been cancelled. Please contact the clinic to rebook."),
            ["hi"] = ("अपॉइंटमेंट रद्द", "{#var#} में {#var#} को {#var#} बजे की आपकी अपॉइंटमेंट रद्द कर दी गई है। दोबारा बुक करने के लिए क्लिनिक से संपर्क करें।"),
            ["ar"] = ("تم إلغاء الموعد", "تم إلغاء موعدك في {#var#} بتاريخ {#var#} الساعة {#var#}. يرجى التواصل مع العيادة لإعادة الحجز."),
        },
        ["SeriesCancelled"] = new()
        {
            ["en"] = ("Appointments cancelled", "Your appointment at {#var#} on {#var#} at {#var#} and your later repeat visits have been cancelled. Please contact the clinic to rebook."),
            ["hi"] = ("अपॉइंटमेंट्स रद्द", "{#var#} में {#var#} को {#var#} बजे की आपकी अपॉइंटमेंट और उसके बाद की दोहराई गई अपॉइंटमेंट रद्द कर दी गई हैं। दोबारा बुक करने के लिए क्लिनिक से संपर्क करें।"),
            ["ar"] = ("تم إلغاء المواعيد", "تم إلغاء موعدك في {#var#} بتاريخ {#var#} الساعة {#var#} ومواعيدك المتكررة اللاحقة. يرجى التواصل مع العيادة لإعادة الحجز."),
        },
        ["NeedsRebooking"] = new()
        {
            ["en"] = ("Please rebook your appointment", "Your appointment at {#var#} on {#var#} at {#var#} cannot go ahead. Please contact the clinic to choose a new time."),
            ["hi"] = ("कृपया अपॉइंटमेंट दोबारा बुक करें", "{#var#} में {#var#} को {#var#} बजे की आपकी अपॉइंटमेंट नहीं हो सकेगी। नया समय चुनने के लिए क्लिनिक से संपर्क करें।"),
            ["ar"] = ("يرجى إعادة حجز الموعد", "تعذر إجراء موعدك في {#var#} بتاريخ {#var#} الساعة {#var#}. يرجى التواصل مع العيادة لاختيار موعد جديد."),
        },
        ["WaitlistOffer"] = new()
        {
            ["en"] = ("A slot is available", "A slot has opened at {#var#} on {#var#} at {#var#}. Please contact the clinic soon to take it."),
            ["hi"] = ("एक स्लॉट उपलब्ध है", "{#var#} में {#var#} को {#var#} बजे एक स्लॉट खाली हुआ है। इसे लेने के लिए जल्द क्लिनिक से संपर्क करें।"),
            ["ar"] = ("يوجد موعد متاح", "توفر موعد في {#var#} بتاريخ {#var#} الساعة {#var#}. يرجى التواصل مع العيادة قريباً لحجزه."),
        },
        ["Reminder"] = new()
        {
            ["en"] = ("Appointment reminder", "Reminder: your appointment at {#var#} is on {#var#} at {#var#}."),
            ["hi"] = ("अपॉइंटमेंट की याद", "याद दिलाना: {#var#} में आपकी अपॉइंटमेंट {#var#} को {#var#} बजे है।"),
            ["ar"] = ("تذكير بالموعد", "تذكير: موعدك في {#var#} بتاريخ {#var#} الساعة {#var#}."),
        },
    };

    /// <summary>Words only the HTML email uses (MDV-P1-111): the SMS and plain-text bodies above never change. SentBy takes the clinic.</summary>
    internal sealed record EmailWords(string Date, string Time, string Clinic, string Change, string Call, string ChangeHow, string Phone, string SentBy, string Why);

    internal static readonly Dictionary<string, EmailWords> Words = new()
    {
        ["en"] = new("Date", "Time", "Clinic", "Change", "Call ", "To move or cancel, call the clinic.", "Phone",
            "Sent by {0} through Medvya. This email has no medical details.", "You get it because you agreed to appointment emails. To stop them, tell the clinic."),
        ["hi"] = new("तारीख", "समय", "क्लिनिक", "बदलाव", "कॉल करें: ", "समय बदलने या रद्द करने के लिए क्लिनिक को कॉल करें।", "फ़ोन",
            "{0} ने Medvya के ज़रिए भेजा। इस ईमेल में कोई चिकित्सा जानकारी नहीं है।", "यह आपको इसलिए मिला क्योंकि आपने अपॉइंटमेंट ईमेल के लिए सहमति दी थी। इन्हें बंद करने के लिए क्लिनिक को बताएँ।"),
        ["ar"] = new("التاريخ", "الوقت", "العيادة", "للتغيير", "اتصل على ", "لنقل الموعد أو إلغائه، اتصل بالعيادة.", "الهاتف",
            "أرسلتها {0} عبر Medvya. لا يحتوي هذا البريد على أي تفاصيل طبية.", "تصلك لأنك وافقت على رسائل المواعيد. لإيقافها، أبلغ العيادة."),
    };

    /// <summary>The facility's time zone by name, so nobody misreads the time; an unknown zone shows its id.</summary>
    static readonly Dictionary<string, Dictionary<string, string>> Zones = new()
    {
        ["Asia/Kolkata"] = new() { ["en"] = "India Standard Time", ["hi"] = "भारतीय मानक समय", ["ar"] = "بتوقيت الهند" },
        ["Asia/Dubai"] = new() { ["en"] = "Gulf Standard Time", ["hi"] = "खाड़ी मानक समय", ["ar"] = "بتوقيت الإمارات" },
    };

    internal static string ZoneName(string timeZone, string lang) => Zones.GetValueOrDefault(timeZone)?.GetValueOrDefault(lang) ?? timeZone;

    internal static CultureInfo Culture(string lang) => CultureInfo.GetCultureInfo(lang switch { "hi" => "hi-IN", "ar" => "ar-AE", _ => "en-IN" });

    /// <summary>One regex per registered text: literal parts exact, each slot 1-30 characters.</summary>
    static readonly Dictionary<string, Regex> Matchers = Texts.SelectMany(g => g.Value.Select(l => (Key: $"{g.Key}.{l.Key}", l.Value.Text)))
        .ToDictionary(t => t.Key, t => new Regex("^" + string.Join(".{1," + MaxSlotLength + "}", t.Text.Split(Slot).Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50)));

    public static string Group(NotificationKind kind) => kind is NotificationKind.Reminder24h or NotificationKind.Reminder2h ? "Reminder" : kind.ToString();

    /// <summary>The supported language of a BCP 47 tag ("hi-IN" gives "hi"); English for anything else.</summary>
    public static string Language(string? tag)
    {
        var primary = tag?.Split('-', '_')[0].ToLowerInvariant();
        return Array.IndexOf(Languages, primary) >= 0 ? primary! : "en";
    }

    public static RenderedMessage Render(NotificationKind kind, string? languageTag, string clinic, DateTimeOffset localStart)
    {
        var lang = Language(languageTag);
        var group = Group(kind);
        var (subject, text) = Texts[group][lang];
        var culture = Culture(lang);
        var values = new[] { Clamp(clinic), localStart.ToString("d MMM yyyy", culture), localStart.ToString("h:mm tt", culture) };
        var i = 0;
        return new RenderedMessage(subject, SlotPattern().Replace(text, _ => values[i++]), $"{group}.{lang}");
    }

    /// <summary>
    /// The HTML copy of a rendered appointment message: the same sentence, then the date spelt out, the time with its zone named, the clinic with its
    /// address and how to reach it. Still only the clinic, the date and the time about the visit: no doctor, reason or medical detail.
    /// </summary>
    /// <param name="sender">The practice's sender name (the masthead); <paramref name="location"/> is the facility, shown under it when different.</param>
    public static EmailView Email(NotificationKind kind, string? languageTag, RenderedMessage text, string sender, string location, DateTimeOffset localStart, string timeZone,
        string? address, string? phone)
    {
        var lang = Language(languageTag);
        var w = Words[lang];
        var culture = Culture(lang);
        List<EmailFact> facts =
        [
            new(w.Date, localStart.ToString("dddd d MMMM yyyy", culture)),
            new(w.Time, localStart.ToString("h:mm tt", culture), ZoneName(timeZone, lang)),
            new(w.Clinic, location, address),
        ];
        if (!string.IsNullOrWhiteSpace(phone))
            facts.Add(Group(kind) is "Confirmation" or "Rescheduled" or "Reminder" ? new(w.Change, w.Call, w.ChangeHow, phone) : new(w.Phone, "", null, phone));
        return new(lang, sender, location == sender ? null : location, text.Subject, [text.Body], facts, null, [],
            [string.Format(culture, w.SentBy, sender), w.Why]);
    }

    /// <summary>Every template in one language with its slots named {clinic}, {date}, {time}: what the settings screen shows. No patient values.</summary>
    public static IReadOnlyList<TemplatePreview> Previews(string? languageTag)
    {
        var lang = Language(languageTag);
        string[] names = ["{clinic}", "{date}", "{time}"];
        return [.. Texts.Select(g =>
        {
            var i = 0;
            return new TemplatePreview(g.Key, g.Value[lang].Subject, SlotPattern().Replace(g.Value[lang].Text, _ => names[i++]));
        })];
    }

    /// <summary>The registered template a final SMS text matches, or null: a text that matches none is never sent (DLT).</summary>
    public static string? RegisteredTemplate(string text) => Matchers.FirstOrDefault(m => m.Value.IsMatch(text)).Key;

    static string Clamp(string value) => value.Length <= MaxSlotLength ? value : value[..MaxSlotLength];

    [GeneratedRegex(@"\{#var#\}")]
    private static partial Regex SlotPattern();
}
