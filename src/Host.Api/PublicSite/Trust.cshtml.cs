using System.Globalization;
using System.Security.Cryptography;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Host.Api.PublicSite;

/// <summary>F-07 trust centre and demo request (MDV-P1-003). The form takes buyer contact details only, never patient data.</summary>
public sealed class TrustModel(SiteText text, IOptionsSnapshot<TrustCentreContent> content, IDataProtectionProvider dataProtection,
    DemoRequestLimiter limiter, IConfiguration config) : PublicPage(text)
{
    public sealed class DemoForm
    {
        public string? Name { get; set; }
        public string? Organisation { get; set; }
        public string? Role { get; set; }
        public string? Country { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Message { get; set; }
        /// <summary>Protected time the form was shown; too fast or too old means a bot or a stale page.</summary>
        public string? Started { get; set; }
        /// <summary>Honeypot, hidden from people. Anything typed here is a bot.</summary>
        public string? Website { get; set; }
    }

    [BindProperty] public DemoForm Form { get; set; } = new();
    public IReadOnlyDictionary<string, string> Errors { get; private set; } = new Dictionary<string, string>();
    /// <summary>Form-level problem: "rateLimited" or "expired".</summary>
    public string? FormError { get; private set; }
    public bool Sent => Request.Query.ContainsKey("sent");
    public TrustCentreContent Lists => content.Value;
    public string Started { get; private set; } = "";

    IDataProtector Protector => dataProtection.CreateProtector("PublicSite.DemoForm.Started");
    TimeSpan MinimumFillTime => TimeSpan.FromSeconds(config.GetValue("PublicSite:DemoMinimumSeconds", 3));

    public void OnGet() => Started = Protector.Protect(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

    public async Task<IActionResult> OnPostAsync([FromServices] IDemoRequests requests, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(Form.Website)) return SentPage(); // bots get the same answer, and nothing is stored

        var request = new DemoRequest
        {
            Name = Clean(Form.Name) ?? "", Organisation = Clean(Form.Organisation) ?? "", Role = Clean(Form.Role) ?? "",
            Country = Clean(Form.Country) ?? "", Email = Clean(Form.Email), Phone = Clean(Form.Phone), Message = Clean(Form.Message),
        };
        if (!StartedInTime()) return Again("expired", StatusCodes.Status400BadRequest);
        Errors = DemoRequestRules.Validate(request);
        if (Errors.Count > 0) return Again(null, StatusCodes.Status400BadRequest);
        if (!limiter.TryAcquire(HttpContext)) return Again("rateLimited", StatusCodes.Status429TooManyRequests);

        await requests.SubmitAsync(request, cancellationToken);
        return SentPage();
    }

    bool StartedInTime()
    {
        try
        {
            var started = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(Protector.Unprotect(Form.Started ?? ""), CultureInfo.InvariantCulture));
            var age = DateTimeOffset.UtcNow - started;
            return age >= MinimumFillTime && age <= TimeSpan.FromDays(1);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or OverflowException)
        {
            return false;
        }
    }

    // Shows the form again with what was typed, a fresh start time and the errors.
    PageResult Again(string? formError, int status)
    {
        FormError = formError;
        Response.StatusCode = status;
        OnGet();
        return Page();
    }

    LocalRedirectResult SentPage() => LocalRedirect($"{Href(PublicSite.TrustCentre)}?sent=1#demo");

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public string FieldError(string field) => Errors.TryGetValue(field, out var code)
        ? T(code == "tooLong" ? "site.demo.error.tooLong" : $"site.demo.error.{field}")
        : "";

    public string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo(Lang switch { "hi" => "hi-IN", "ar" => "ar-AE", _ => "en-IN" }));

    public string Localized(IDictionary<string, string> values) => values.TryGetValue(Lang, out var v) ? v : values.TryGetValue("en", out var en) ? en : "";
}
