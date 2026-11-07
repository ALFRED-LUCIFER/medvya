using Microsoft.Extensions.Options;

namespace Host.Api.PublicSite;

/// <summary>F-01 pricing (MDV-P5-050): the approved plans in the region's currency, monthly or yearly by a plain link (no script).</summary>
public sealed class PricingModel(SiteText text, IOptionsSnapshot<PricingContent> content) : PublicPage(text)
{
    public PricingContent List => content.Value;
    public bool Yearly { get; private set; }

    public void OnGet(string? cycle) => Yearly = cycle == "yearly";

    public PlanPrice? PriceOf(PricingPlan plan) => plan.Prices.TryGetValue(Region, out var p) ? p : null;

    /// <summary>Region step with the country and plan code already chosen, so it goes straight on to sign-up (MDV-P1-004).</summary>
    public string TrialHref(PricingPlan plan) => $"{Href(PublicSite.DoctorStart)}?go={Region}&plan={Uri.EscapeDataString(plan.Code)}";
}
