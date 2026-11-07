using Microsoft.Extensions.Options;

namespace Host.Api.PublicSite;

/// <summary>F-01 Medvya Assist (MDV-P1-055): how AI notes work and the safety rules. The add-on price comes only from the price list.</summary>
public sealed class AssistModel(SiteText text, IOptionsSnapshot<PricingContent> pricing) : PublicPage(text)
{
    public string? AddOnPrice => pricing.Value.AssistMonthly(Region) is { } price ? Isolate(price) : null;

    public void OnGet() { }
}
