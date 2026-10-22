using System.Globalization;
using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Host.Api.PublicSite;

/// <summary>F-07 trust centre and demo request (MDV-P1-003). The form takes buyer contact details only, never patient data.</summary>
public sealed class TrustModel(SiteText text, IOptionsSnapshot<TrustCentreContent> content, PublicFormGuard guard) : PublicFormPage(text, guard)
{
    public sealed class DemoForm : ContactForm
    {
        public string? Organisation { get; set; }
        public string? Role { get; set; }
        public string? Country { get; set; }
        public string? Message { get; set; }
    }

    [BindProperty] public DemoForm Form { get; set; } = new();
    public override ContactForm Contact => Form;
    public override string FormId => "demo";
    public override IReadOnlyList<(string Field, string Id)> Fields { get; } =
    [
        ("Name", "demo-name"), ("Organisation", "demo-organisation"), ("Role", "demo-role"), ("Country", "demo-country"),
        ("Contact", "demo-email"), ("Email", "demo-email"), ("Phone", "demo-phone"), ("Message", "demo-message"),
    ];
    protected override string ErrorKey(string field) => field is "Organisation" or "Role" ? $"site.demo.error.{field}" : base.ErrorKey(field);

    public TrustCentreContent Lists => content.Value;

    public void OnGet() => NewForm();

    public Task<IActionResult> OnPostAsync([FromServices] IDemoRequests requests, CancellationToken cancellationToken)
    {
        var request = new DemoRequest
        {
            Name = Clean(Form.Name) ?? "", Organisation = Clean(Form.Organisation) ?? "", Role = Clean(Form.Role) ?? "",
            Country = Clean(Form.Country) ?? "", Email = Clean(Form.Email), Phone = Clean(Form.Phone), Message = Clean(Form.Message),
        };
        return SubmitAsync(DemoRequestRules.Validate(request), () => requests.SubmitAsync(request, cancellationToken),
            $"{Href(PublicSite.TrustCentre)}?sent=1#demo");
    }

    public string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo(Lang switch { "hi" => "hi-IN", "ar" => "ar-AE", _ => "en-IN" }));

    public string Localized(IDictionary<string, string> values) => values.TryGetValue(Lang, out var v) ? v : values.TryGetValue("en", out var en) ? en : "";
}
