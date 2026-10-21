using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Host.Api.PublicSite;

/// <summary>Language (from the address), region (from a preference cookie) and copy for every public page.</summary>
public abstract class PublicPage(SiteText text) : PageModel
{
    public string Lang => RouteData.Values["lang"] as string ?? "en";
    public string Dir => Lang == "ar" ? "rtl" : "ltr";
    public string Region { get; private set; } = PublicSite.DefaultRegion;

    /// <summary>This page's address without the language prefix, e.g. "/" or "/pricing".</summary>
    public string PagePath => PageContext.ActionDescriptor.ViewEnginePath is "/Index" or null ? "/" : PageContext.ActionDescriptor.ViewEnginePath.ToLowerInvariant();

    public string T(string key) => text.Get(Lang, key);
    public string T(string key, IReadOnlyDictionary<string, string> vars) => text.Get(Lang, key, vars);
    /// <summary>Region-specific copy: key.IN or key.AE.</summary>
    public string TRegion(string key) => text.Get(Lang, $"{key}.{Region}");

    public string Href(string path, string? lang = null) => (lang ?? Lang) switch
    {
        "en" => path,
        var l => path == "/" ? $"/{l}" : $"/{l}{path}",
    };

    public string AbsoluteUrl(string path, string? lang = null) => $"{Request.Scheme}://{Request.Host}{Href(path, lang)}";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        // ?region=AE (the region switch) stores the choice and returns to the clean address; links work without JavaScript.
        if (Request.Query["region"] is [{ } picked] && PublicSite.Regions.Contains(picked))
        {
            // Non-tracking: the cookie holds only the region code, no identifier.
            Response.Cookies.Append(PublicSite.RegionCookie, picked, new CookieOptions
            {
                HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(365),
            });
            context.Result = LocalRedirect(Href(PagePath));
            return;
        }
        if (Request.Cookies[PublicSite.RegionCookie] is { } saved && PublicSite.Regions.Contains(saved)) Region = saved;
    }
}
