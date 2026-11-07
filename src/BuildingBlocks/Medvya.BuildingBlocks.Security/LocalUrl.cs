namespace Medvya.BuildingBlocks.Security;

/// <summary>The sign-in return-address check, shared by the API and the platform console host (#876).</summary>
public static class LocalUrl
{
    /// <summary>
    /// True only for a path on this site. Refuses "//host" and "/\host", and any control character or backslash anywhere: browsers drop
    /// tabs and newlines while parsing, so "/\t/evil.example" would otherwise leave the site after sign-in.
    /// </summary>
    public static bool IsLocal(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.Any(c => c < ' ' || c == '\u007f' || c == '\\');
}
