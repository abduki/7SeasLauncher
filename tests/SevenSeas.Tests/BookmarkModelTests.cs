using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

/// <summary>
/// Bookmarks and websites are one concept: browsability comes from the address, and searchability
/// is an optional property of the same bookmark.
/// </summary>
public sealed class BookmarkModelTests
{
    private static SiteScheme BrowseOnly() => new()
    {
        Name = "Just Browsing",
        BaseUrl = "https://example.com",
        SearchUrlTemplate = null,
    };

    private static SiteScheme Searchable() => new()
    {
        Name = "Searchable",
        BaseUrl = "https://example.com",
        SearchUrlTemplate = "https://example.com/search?q={query}",
    };

    [Fact]
    public void ABrowseOnlyBookmark_IsValidButCannotSearch()
    {
        var bookmark = BrowseOnly();

        Assert.True(bookmark.TryValidate(out var error), error);
        Assert.False(bookmark.CanSearch);
        Assert.Null(bookmark.BuildSearchUrl("zelda"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySearchTemplate_MeansNotSearchable(string? template)
    {
        var bookmark = BrowseOnly();
        bookmark.SearchUrlTemplate = template;

        Assert.True(bookmark.IsValid);
        Assert.False(bookmark.CanSearch);
    }

    [Fact]
    public void ASearchTemplate_IsWhatMakesABookmarkSearchable()
    {
        var bookmark = Searchable();

        Assert.True(bookmark.IsValid);
        Assert.True(bookmark.CanSearch);
        Assert.Equal("https://example.com/search?q=Hollow%20Knight", bookmark.BuildSearchUrl("Hollow Knight"));
    }

    [Fact]
    public void APresentButBrokenSearchTemplate_IsStillRejected()
    {
        // Silently ignoring a malformed template would make Search look broken.
        var bookmark = BrowseOnly();
        bookmark.SearchUrlTemplate = "https://example.com/search";

        Assert.False(bookmark.TryValidate(out var error));
        Assert.Contains("{query}", error, StringComparison.Ordinal);
        Assert.Contains("Leave it empty", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnusableAddress_IsRejectedWhetherOrNotItCanSearch()
    {
        var bookmark = new SiteScheme { Name = "Bad", BaseUrl = "not a url" };

        Assert.False(bookmark.TryValidate(out var error));
        Assert.Contains("http(s)", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ABookmarkNeedsAName()
    {
        var bookmark = new SiteScheme { Name = "  ", BaseUrl = "https://example.com" };

        Assert.False(bookmark.TryValidate(out var error));
        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class BookmarkMigrationTests
{
    private static (SchemeLoader Loader, FakeSettingsService Settings) Create(TempWorkspace workspace)
    {
        var loader = new SchemeLoader(workspace.Resolve("schemes"));
        return (loader, new FakeSettingsService());
    }

    [Fact]
    public void Migrate_TurnsHomePagesIntoBrowseOnlyBookmarks()
    {
        using var workspace = new TempWorkspace();
        var (loader, settings) = Create(workspace);
        settings.Update(s =>
        {
            s.HomePages.Add(new HomePage { Name = "FMHY", Url = "https://fmhy.net/" });
            s.HomePages.Add(new HomePage { Name = "Example", Url = "https://example.com" });
        });

        var moved = BookmarkMigration.Migrate(settings, loader);

        Assert.Equal(2, moved);
        Assert.Empty(settings.Current.HomePages);

        var bookmarks = loader.LoadAll();
        Assert.Equal(2, bookmarks.Count);
        Assert.All(bookmarks, b => Assert.False(b.CanSearch));
        Assert.Contains(bookmarks, b => b.Name == "FMHY" && b.BaseUrl == "https://fmhy.net/");
    }

    [Fact]
    public void Migrate_DoesNotDuplicateAnAddressAlreadySavedAsABookmark()
    {
        using var workspace = new TempWorkspace();
        var (loader, settings) = Create(workspace);
        loader.Save(new SiteScheme
        {
            Name = "Example",
            BaseUrl = "https://example.com/",
            SearchUrlTemplate = "https://example.com/search?q={query}",
        });
        settings.Update(s => s.HomePages.Add(new HomePage { Name = "Example", Url = "https://example.com" }));

        BookmarkMigration.Migrate(settings, loader);

        Assert.Empty(settings.Current.HomePages);
        Assert.Single(loader.LoadAll()); // the existing searchable bookmark survived untouched
    }

    [Fact]
    public void Migrate_KeepsGoallessEntriesOutOfBookmarks()
    {
        using var workspace = new TempWorkspace();
        var (loader, settings) = Create(workspace);
        settings.Update(s => s.HomePages.Add(new HomePage { Name = "Blank", Url = string.Empty }));

        BookmarkMigration.Migrate(settings, loader);

        Assert.Empty(loader.LoadAll());
        Assert.Empty(settings.Current.HomePages);
    }

    [Fact]
    public void Migrate_NamesAnUnnamedPageFromItsHost()
    {
        using var workspace = new TempWorkspace();
        var (loader, settings) = Create(workspace);
        settings.Update(s => s.HomePages.Add(new HomePage { Name = string.Empty, Url = "https://animex.one/home" }));

        BookmarkMigration.Migrate(settings, loader);

        Assert.Equal("Animex", Assert.Single(loader.LoadAll()).Name);
    }

    [Fact]
    public void Migrate_WithNoHomePages_DoesNothing()
    {
        using var workspace = new TempWorkspace();
        var (loader, settings) = Create(workspace);

        Assert.Equal(0, BookmarkMigration.Migrate(settings, loader));
        Assert.Empty(loader.LoadAll());
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        using var workspace = new TempWorkspace();
        var (loader, settings) = Create(workspace);
        settings.Update(s => s.HomePages.Add(new HomePage { Name = "FMHY", Url = "https://fmhy.net/" }));

        BookmarkMigration.Migrate(settings, loader);
        BookmarkMigration.Migrate(settings, loader);

        Assert.Single(loader.LoadAll());
    }
}
