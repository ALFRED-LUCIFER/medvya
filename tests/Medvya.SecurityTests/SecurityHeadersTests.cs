using Medvya.BuildingBlocks.Security;

namespace Medvya.SecurityTests;

/// <summary>The viewer shows a signed document link from the private Blob origin (MDV-P2-006); nothing else may be framed or loaded from elsewhere.</summary>
public class SecurityHeadersTests
{
    [Fact]
    public void WithoutAStorageOriginNothingOutsideThisOriginIsFramed()
    {
        var csp = SecurityHeaders.PolicyFor(null);
        Assert.DoesNotContain("frame-src", csp, StringComparison.Ordinal); // default-src 'self' governs frames
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStorageOriginMayServeImagesAndFramesAndNothingElse()
    {
        var csp = SecurityHeaders.PolicyFor("https://stmedvya.blob.core.windows.net");
        Assert.Contains("img-src 'self' data: blob: https://stmedvya.blob.core.windows.net;", csp, StringComparison.Ordinal);
        Assert.EndsWith("; frame-src https://stmedvya.blob.core.windows.net", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self';", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self';", csp, StringComparison.Ordinal);
    }
}
