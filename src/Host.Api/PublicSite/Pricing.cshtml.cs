namespace Host.Api.PublicSite;

/// <summary>Pricing placeholder (F-01: no invented prices or metrics).</summary>
public sealed class PricingModel(SiteText text) : PublicPage(text)
{
    public void OnGet() { }
}
