using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class SchemeLoaderTests
{
    private const string ValidScheme = """
    {
      "name": "Example Site",
      "baseUrl": "https://example-game-site.test",
      "searchUrlTemplate": "https://example-game-site.test/search?q={query}",
      "archivePasswordHint": "secret",
      "notes": "fictional"
    }
    """;

    private static SiteScheme NewScheme(string name = "My Site") => new()
    {
        Name = name,
        BaseUrl = "https://my-site.test",
        SearchUrlTemplate = "https://my-site.test/search?q={query}",
        ArchivePasswordHint = null,
        Notes = "added by the user",
    };

    [Fact]
    public void LoadAll_ParsesValidScheme()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/example-site.json", ValidScheme);
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        var schemes = loader.LoadAll();

        Assert.Single(schemes);
        Assert.Equal("Example Site", schemes[0].Name);
        Assert.NotNull(schemes[0].SourceFile);
    }

    [Fact]
    public void BuildSearchUrl_UrlEncodesQuery()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/example-site.json", ValidScheme);
        var loader = new SchemeLoader(workspace.Resolve("schemes"));
        var scheme = loader.GetByName("Example Site")!;

        Assert.Equal("https://example-game-site.test/search?q=Hollow%20Knight", scheme.BuildSearchUrl("Hollow Knight"));
    }

    [Fact]
    public void LoadAll_SkipsMalformedJson()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/broken.json", "{ not json ");
        workspace.CreateFile("schemes/example-site.json", ValidScheme);
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        Assert.Single(loader.LoadAll());
    }

    [Fact]
    public void LoadAll_AcceptsABrowseOnlyBookmark()
    {
        // No search template is the normal state for a bookmark you have not taught to search yet.
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/bookmark.json", """{ "name": "Just Browsing", "baseUrl": "https://x.test" }""");
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        var bookmark = Assert.Single(loader.LoadAll());
        Assert.False(bookmark.CanSearch);
        Assert.Null(bookmark.BuildSearchUrl("anything"));
    }

    [Fact]
    public void LoadAll_SkipsBookmarkWithAnUnusableAddress()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/incomplete.json", """{ "name": "Nope", "baseUrl": "not a url" }""");
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        Assert.Empty(loader.LoadAll());
    }

    [Fact]
    public void LoadAll_SkipsDuplicateNames()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/a.json", ValidScheme);
        workspace.CreateFile("schemes/b.json", ValidScheme);
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        Assert.Single(loader.LoadAll());
    }

    [Fact]
    public void LoadAll_ReturnsEmptyWhenDirectoryMissing()
    {
        using var workspace = new TempWorkspace();
        var loader = new SchemeLoader(workspace.Resolve("no-such-folder"));

        Assert.Empty(loader.LoadAll());
    }

    [Fact]
    public void GetPasswordHint_MatchesByHost()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("schemes/example-site.json", ValidScheme);
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        Assert.Equal("secret", loader.GetPasswordHint("https://example-game-site.test/game/1"));
        Assert.Null(loader.GetPasswordHint("https://other.test/game/1"));
        Assert.Null(loader.GetPasswordHint(null));
    }

    // ---------- Write path: adding and removing websites ----------

    [Fact]
    public void Save_WritesSluggedFileAndReloads()
    {
        using var workspace = new TempWorkspace();
        var directory = workspace.Resolve("schemes");
        var loader = new SchemeLoader(directory);

        var saved = loader.Save(NewScheme("My Cool Site"));

        Assert.True(File.Exists(workspace.Resolve("schemes/my-cool-site.json")));
        Assert.NotNull(saved.SourceFile);

        var reloaded = new SchemeLoader(directory).LoadAll();
        Assert.Single(reloaded);
        Assert.Equal("My Cool Site", reloaded[0].Name);
        Assert.Equal("added by the user", reloaded[0].Notes);
    }

    [Fact]
    public void Save_DoesNotPersistComputedMembers()
    {
        using var workspace = new TempWorkspace();
        var loader = new SchemeLoader(workspace.Resolve("schemes"));
        loader.Save(NewScheme());

        var json = File.ReadAllText(workspace.Resolve("schemes/my-site.json"));

        Assert.DoesNotContain("isValid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceFile", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_RejectsInvalidScheme()
    {
        using var workspace = new TempWorkspace();
        var loader = new SchemeLoader(workspace.Resolve("schemes"));
        var bad = NewScheme();
        bad.SearchUrlTemplate = "https://my-site.test/search"; // missing {query}

        var ex = Assert.Throws<ArgumentException>(() => loader.Save(bad));

        Assert.Contains("{query}", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "https://x.test", "https://x.test/?q={query}", "name")]
    [InlineData("X", "not-a-url", "https://x.test/?q={query}", "http(s)")]
    [InlineData("X", "https://x.test", "https://x.test/?q={query}", "")]
    public void TryValidate_ReportsUsefulErrors(string name, string baseUrl, string template, string expectedFragment)
    {
        var scheme = new SiteScheme { Name = name, BaseUrl = baseUrl, SearchUrlTemplate = template };

        var valid = scheme.TryValidate(out var error);

        if (expectedFragment.Length == 0)
        {
            Assert.True(valid, error);
        }
        else
        {
            Assert.False(valid);
            Assert.Contains(expectedFragment, error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Save_WithRename_RemovesTheOldFile()
    {
        using var workspace = new TempWorkspace();
        var loader = new SchemeLoader(workspace.Resolve("schemes"));
        var original = loader.Save(NewScheme("Old Name"));
        Assert.True(File.Exists(workspace.Resolve("schemes/old-name.json")));

        var renamed = NewScheme("New Name");
        renamed.SourceFile = original.SourceFile;
        loader.Save(renamed, original.Name);

        Assert.False(File.Exists(workspace.Resolve("schemes/old-name.json")));
        Assert.True(File.Exists(workspace.Resolve("schemes/new-name.json")));
        Assert.Single(loader.LoadAll());
    }

    [Fact]
    public void Delete_RemovesTheFile()
    {
        using var workspace = new TempWorkspace();
        var loader = new SchemeLoader(workspace.Resolve("schemes"));
        var saved = loader.Save(NewScheme());

        var deleted = loader.Delete(saved);

        Assert.True(deleted);
        Assert.Empty(loader.LoadAll());
        Assert.False(File.Exists(workspace.Resolve("schemes/my-site.json")));
    }

    [Fact]
    public void Delete_UnknownScheme_ReturnsFalse()
    {
        using var workspace = new TempWorkspace();
        var loader = new SchemeLoader(workspace.Resolve("schemes"));

        Assert.False(loader.Delete(NewScheme("Never Saved")));
    }

    [Fact]
    public void Refresh_PicksUpFilesAddedOnDisk()
    {
        using var workspace = new TempWorkspace();
        var directory = workspace.Resolve("schemes");
        var loader = new SchemeLoader(directory);
        Assert.Empty(loader.LoadAll());

        workspace.CreateFile("schemes/example-site.json", ValidScheme);
        Assert.Empty(loader.LoadAll()); // cached

        loader.Refresh();

        Assert.Single(loader.LoadAll());
    }
}
