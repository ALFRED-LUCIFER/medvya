namespace Host.Api.PublicSite;

/// <summary>F-01 landing.</summary>
public sealed class IndexModel(SiteText text) : PublicPage(text)
{
    public void OnGet() { }
}
