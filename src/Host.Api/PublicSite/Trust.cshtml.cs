using Microsoft.Extensions.Options;

namespace Host.Api.PublicSite;

/// <summary>F-07 trust centre (MDV-P1-003, MDV-P1-058): controls, compliance status and dated lists from trust-centre.json. The demo form is on /demo.</summary>
public sealed class TrustModel(SiteText text, IOptionsSnapshot<TrustCentreContent> content) : PublicPage(text)
{
    public TrustCentreContent Lists => content.Value;

    public void OnGet() { }

    public string Localized(IDictionary<string, string> values) => values.TryGetValue(Lang, out var v) ? v : values.TryGetValue("en", out var en) ? en : "";
}
