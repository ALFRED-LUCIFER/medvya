namespace Host.Api.PublicSite;

/// <summary>Policy versions and sub-processors on the trust centre, from PublicSite/trust-centre.json (reloads on change).</summary>
public sealed class TrustCentreContent
{
    public string Version { get; set; } = "";
    public DateOnly? Updated { get; set; }
    public IList<TrustPolicy> Policies { get; } = [];
    public IList<TrustSubProcessor> SubProcessors { get; } = [];
}

public sealed class TrustPolicy
{
    /// <summary>Per language (en, hi, ar); English when a translation is missing.</summary>
    public IDictionary<string, string> Name { get; } = new Dictionary<string, string>();
    public string Version { get; set; } = "";
    /// <summary>Empty until signed off: shown as "Not yet in effect".</summary>
    public DateOnly? Effective { get; set; }
}

public sealed class TrustSubProcessor
{
    public string Name { get; set; } = "";
    public IDictionary<string, string> Purpose { get; } = new Dictionary<string, string>();
    /// <summary>Per country deployment (IN, AE); empty while being confirmed.</summary>
    public IDictionary<string, string> Region { get; } = new Dictionary<string, string>();
    public DateOnly? Effective { get; set; }
}
