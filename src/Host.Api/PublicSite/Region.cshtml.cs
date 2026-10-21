using Medvya.Leads.Application;
using Medvya.Leads.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Host.Api.PublicSite;

/// <summary>Country deployments that take sign-ups (ADR-003: one deployment, one data plane per country).</summary>
public sealed class DeploymentOptions
{
    /// <summary>
    /// Sign-up address of each live country deployment, by country (IN, AE). A country without one is not live, so the region
    /// selector offers the waitlist instead. shortcut: none is set until onboarding (MDV-P1-004) ships in the launch market (MDV-P0-003).
    /// </summary>
    public IDictionary<string, string> SignUpUrls { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// F-02 "Where do you practise?" (MDV-P1-002). Choosing a country picks the deployment, and so the data plane, a new practice is
/// created in; a country that is not live yet takes waitlist details (no patient data) instead.
/// </summary>
public sealed class RegionModel(SiteText text, IOptionsSnapshot<DeploymentOptions> deployments, PublicFormGuard guard) : PublicFormPage(text, guard)
{
    public sealed class WaitlistForm : ContactForm
    {
        public string? Country { get; set; }
    }

    [BindProperty] public WaitlistForm Form { get; set; } = new();
    public override ContactForm Contact => Form;
    public override string FormId => "waitlist";
    public override IReadOnlyList<(string Field, string Id)> Fields { get; } =
        [("Name", "waitlist-name"), ("Contact", "waitlist-email"), ("Email", "waitlist-email"), ("Phone", "waitlist-phone")];

    /// <summary>The not-yet-live country whose waitlist is shown, if any.</summary>
    public string? WaitlistCountry { get; private set; }

    public IActionResult OnGet(string? go)
    {
        NewForm();
        if (go is null || !PublicSite.Regions.Contains(go)) return Page();
        // shortcut: the choice is recorded in the onboarding audit trail when the tenant is created (MDV-P1-004); before that no tenant exists to audit against.
        if (deployments.Value.SignUpUrls.TryGetValue(go, out var signUp)) return Redirect($"{signUp}{(signUp.Contains('?', StringComparison.Ordinal) ? '&' : '?')}country={go}");
        WaitlistCountry = go;
        return Page();
    }

    public Task<IActionResult> OnPostAsync([FromServices] IWaitlist waitlist, CancellationToken cancellationToken)
    {
        if (Form.Country is not { } country || !PublicSite.Regions.Contains(country)) return Task.FromResult<IActionResult>(BadRequest());
        WaitlistCountry = country;
        var entry = new WaitlistEntry { Name = Clean(Form.Name) ?? "", Country = country, Email = Clean(Form.Email), Phone = Clean(Form.Phone) };
        return SubmitAsync(WaitlistRules.Validate(entry), () => waitlist.JoinAsync(entry, cancellationToken),
            $"{Href(PublicSite.DoctorStart)}?go={country}&sent=1#waitlist");
    }

    public string RegionName(string code) => T($"site.region.{code}");
}
