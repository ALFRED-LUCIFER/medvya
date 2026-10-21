using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.WebEncoders;

namespace Host.Api.PublicSite;

/// <summary>
/// Public marketing site (MDV-P1-001, ADR-001): server-rendered Razor Pages with no JavaScript and no patient or tenant data.
/// Its endpoints run before authentication (<see cref="RunPublicSiteBeforeAuthentication"/>), so they never read the staff session cookie.
/// </summary>
public static partial class PublicSite
{
    public const string RegionCookie = "medvya-region";
    public static readonly string[] Regions = ["IN", "AE"];
    // The proposed launch market (ADR-002); only the copy a first-time visitor sees depends on it.
    public const string DefaultRegion = "IN";
    public static readonly string[] Languages = ["en", "hi", "ar"];
    // Each language in its own script, as in the clinic app's language picker.
    public static readonly Dictionary<string, string> LanguageNames = new() { ["en"] = "English", ["hi"] = "हिन्दी", ["ar"] = "العربية" };

    // Page addresses without the language prefix. Region, trust centre and demo request arrive with MDV-P1-002 and MDV-P1-003.
    public const string DoctorStart = "/region";
    public const string TrustCentre = "/trust";
    public const string DemoRequest = "/trust#demo";
    public const string Pricing = "/pricing";

    sealed class PublicSiteEndpoint;

    public static IServiceCollection AddPublicSite(this IServiceCollection services)
    {
        services.AddRazorPages(o =>
        {
            o.RootDirectory = "/PublicSite";
            o.Conventions.AllowAnonymousToFolder("/"); // public: marketing pages, no patient or tenant data
            // English is unprefixed; Hindi and Arabic live under /hi and /ar so each language has its own indexable address.
            o.Conventions.AddFolderRouteModelConvention("/", model =>
            {
                foreach (var selector in model.Selectors.ToList())
                    model.Selectors.Add(new SelectorModel
                    {
                        AttributeRouteModel = new AttributeRouteModel
                        {
                            Template = AttributeRouteModel.CombineTemplates("{lang:regex(^(hi|ar)$)}", selector.AttributeRouteModel?.Template),
                        },
                    });
            });
        });
        services.AddSingleton<SiteText>();
        // Write Hindi and Arabic as characters, not &#x...; entities (markup characters are still encoded).
        services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
        return services;
    }

    public static WebApplication MapPublicSite(this WebApplication app)
    {
        app.MapGet(StylesheetPath, (HttpContext ctx) =>
            {
                ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable"; // the address carries a content hash
                return Results.Text(Css.Value.Content, "text/css; charset=utf-8");
            })
            .AllowAnonymous() // public: stylesheet
            .WithMetadata(new PublicSiteEndpoint());
        app.MapRazorPages().WithMetadata(new PublicSiteEndpoint());
        return app;
    }

    /// <summary>Runs public-site endpoints straight away, ahead of authentication, CSRF and authorization middleware.</summary>
    public static IApplicationBuilder RunPublicSiteBeforeAuthentication(this IApplicationBuilder app) => app.Use((ctx, next) =>
        ctx.GetEndpoint() is { RequestDelegate: { } run } endpoint && endpoint.Metadata.GetMetadata<PublicSiteEndpoint>() is not null
            ? run(ctx)
            : next(ctx));

    const string StylesheetPath = "/site.css";
    static readonly Lazy<(string Content, string Hash)> Css = new(Stylesheet);
    public static string StylesheetHref => $"{StylesheetPath}?v={Css.Value.Hash}";

    // Colours, radii and fonts come from the shared design tokens (packages/ui/src/styles.css), so the site cannot drift from the apps.
    static (string Content, string Hash) Stylesheet()
    {
        var tokens = ThemeBlock().Match(Resource("tokens.css"));
        if (!tokens.Success) throw new InvalidOperationException("Design tokens (@theme static) not found in packages/ui/src/styles.css.");
        var content = $":root {{{tokens.Groups[1].Value}}}\n{Resource("site.css")}";
        return (content, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12]);
    }

    internal static string Resource(string name)
    {
        using var stream = typeof(PublicSite).Assembly.GetManifestResourceStream($"PublicSite.{name}")
            ?? throw new InvalidOperationException($"Embedded resource PublicSite.{name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"@theme static \{([^}]*)\}")]
    private static partial Regex ThemeBlock();
}
