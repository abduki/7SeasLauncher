using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class SiteNamingTests
{
    [Theory]
    [InlineData("https://www.animex.one/home", "Animex")]
    [InlineData("https://example-game-site.test/search", "Example Game Site")]
    [InlineData("https://fitgirl-repacks.site/", "Fitgirl Repacks")]
    [InlineData("https://example.com", "Example")]
    public void FromUrl_BuildsReadableBrand(string url, string expected)
    {
        Assert.Equal(expected, SiteNaming.FromUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public void FromUrl_FallsBackWhenUnparseable(string? url)
    {
        Assert.Equal("New Site", SiteNaming.FromUrl(url));
    }

    [Theory]
    [InlineData("Animex - Watch Anime Online", "https://animex.one", "Animex")]
    [InlineData("FitGirl Repacks | Direct Download", "https://fitgirl.test", "FitGirl Repacks")]
    [InlineData("Search results", "https://example.com", "Example")]
    [InlineData("Just a moment...", "https://example.com", "Example")]
    [InlineData("", "https://example.com", "Example")]
    // Browser error pages title themselves with the bare host name.
    [InlineData("example-game-site.test", "https://example-game-site.test/", "Example Game Site")]
    [InlineData("animex.one", "https://animex.one/home", "Animex")]
    public void DisplayName_PrefersTheUsefulPartOfTheTitle(string title, string url, string expected)
    {
        Assert.Equal(expected, SiteNaming.DisplayName(title, url));
    }

    [Fact]
    public void BuildSearchTemplate_JoinsActionAndParameter()
    {
        Assert.Equal("https://site.test/find?q={query}", SiteNaming.BuildSearchTemplate("https://site.test/find", "q"));
        Assert.Equal("https://site.test/s?x=1&q={query}", SiteNaming.BuildSearchTemplate("https://site.test/s?x=1", "q"));
        Assert.Equal("https://site.test/find?search={query}", SiteNaming.BuildSearchTemplate("https://site.test/find", "search"));
    }

    [Fact]
    public void BuildSearchTemplate_DefaultsParameterAndHandlesEmptyAction()
    {
        Assert.Equal("https://site.test/?q={query}", SiteNaming.BuildSearchTemplate("https://site.test/", null));
        Assert.Equal(string.Empty, SiteNaming.BuildSearchTemplate(null, "q"));
    }

    [Theory]
    [InlineData("https://site.test/game/123?x=1", "https://site.test/")]
    [InlineData("https://sub.site.test/a/b", "https://sub.site.test/")]
    [InlineData("about:blank", null)]
    [InlineData(null, null)]
    public void SiteRoot_ReducesToOrigin(string? url, string? expected)
    {
        Assert.Equal(expected, SiteNaming.SiteRoot(url));
    }

    [Theory]
    [InlineData("https://www.Example.com/x", "example.com")]
    [InlineData("https://example.com", "example.com")]
    [InlineData("about:blank", null)]
    [InlineData("not a url", null)]
    public void NormalizedHost_StripsWwwAndLowercases(string? url, string? expected)
    {
        Assert.Equal(expected, SiteNaming.NormalizedHost(url));
    }

    [Fact]
    public void ResolveHomeUrl_PrefersTheRegisteredWebsiteHome()
    {
        // A site's real home is often not its bare origin, so the scheme's baseUrl wins.
        var target = SiteNaming.ResolveHomeUrl(
            "https://example.com/game/7?ref=1",
            new[] { "https://example.com/home", "https://other.test/" },
            "https://fallback.test/");

        Assert.Equal("https://example.com/home", target);
    }

    [Fact]
    public void ResolveHomeUrl_MatchesIgnoringWwwAndCase()
    {
        var target = SiteNaming.ResolveHomeUrl(
            "https://www.Example.com/deep/page",
            new[] { "https://example.com/home" },
            null);

        Assert.Equal("https://example.com/home", target);
    }

    [Fact]
    public void ResolveHomeUrl_BookmarkedSite_UsesTheSiteRootNotTheBookmark()
    {
        // The user is on a bookmarked page of a site we do not have a scheme for.
        var target = SiteNaming.ResolveHomeUrl(
            "https://animex.one/watch/12345?ep=3",
            new[] { "https://example.com/home" },
            "https://fallback.test/");

        Assert.Equal("https://animex.one/", target);
    }

    [Fact]
    public void ResolveHomeUrl_NoPageLoaded_FallsBackToTheSelectedWebsite()
    {
        Assert.Equal(
            "https://fallback.test/",
            SiteNaming.ResolveHomeUrl("about:blank", new[] { "https://example.com/home" }, "https://fallback.test/"));
    }

    [Fact]
    public void ResolveHomeUrl_ReturnsNullWhenThereIsNowhereToGo()
    {
        Assert.Null(SiteNaming.ResolveHomeUrl(null, null, null));
    }

    [Theory]
    [InlineData("https://example-game-site.test/", true)]
    [InlineData("https://fmhy.net/gaming", false)]
    [InlineData("http://localhost:8977/page", true)]
    [InlineData("https://example.com", false)]
    [InlineData("not a url", false)]
    public void IsPlaceholderAddress_SpotsReservedHosts(string url, bool expected)
    {
        Assert.Equal(expected, SiteNaming.IsPlaceholderAddress(url));
    }

    [Fact]
    public void HomePage_ValidatesUrls()
    {
        var good = new HomePage { Name = "Search", Url = "https://example.com" };
        var bad = new HomePage { Name = "Broken", Url = "not a url" };

        Assert.True(good.TryValidate(out _));
        Assert.False(bad.TryValidate(out var error));
        Assert.Contains("not a valid", error, StringComparison.OrdinalIgnoreCase);
    }
}
