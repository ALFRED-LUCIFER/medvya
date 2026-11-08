using Medvya.BuildingBlocks.Security;

namespace Medvya.SecurityTests;

/// <summary>#876: the sign-in return address may only be a path on this site.</summary>
public class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/app/front-desk")]
    [InlineData("/settings?tab=staff#top")]
    public void PathsOnThisSiteAreLocal(string url) => Assert.True(LocalUrl.IsLocal(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/\t/evil.example")]
    [InlineData("/\n/evil.example")]
    [InlineData("/\r/evil.example")]
    [InlineData("/x/\\/evil.example")]
    [InlineData("/\u007f/evil.example")]
    [InlineData("evil.example")]
    public void AnythingThatCanLeaveTheSiteIsNot(string? url) => Assert.False(LocalUrl.IsLocal(url));
}
