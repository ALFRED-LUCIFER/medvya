namespace Host.Api.PublicSite;

/// <summary>F-01 for hospitals (MDV-P1-056). Every block says whether it is available or planned; nothing unbuilt is offered as available.</summary>
public sealed class HospitalsModel(SiteText text) : PublicPage(text)
{
    /// <summary>What hospitals get, in the designed order. Set Available only once the feature ships (PublicSiteTests checks the list).</summary>
    public static readonly (string Key, bool Available)[] Modules =
    [
        ("opd", true), ("audit", true), ("departments", false), ("insurance", false), ("lab", false), ("reports", false),
        ("sso", false), ("app", false), ("inpatient", false), ("pharmacy", false), ("his", false),
    ];

    public void OnGet() { }
}
