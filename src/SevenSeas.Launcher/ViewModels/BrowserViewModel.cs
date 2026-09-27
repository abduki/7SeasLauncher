using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>
/// State for the embedded browser.
/// <para>
/// There is one list here, not two: every entry is a bookmark. Clicking one navigates to it, and
/// the selected bookmark is also what Home opens and what Search targets.
/// </para>
/// </summary>
public partial class BrowserViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ISchemeLoader _bookmarks;
    private readonly StatusService _status;

    [ObservableProperty]
    private BookmarkItemViewModel? selectedBookmark;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string address = string.Empty;

    [ObservableProperty]
    private bool canGoBack;

    [ObservableProperty]
    private bool canGoForward;

    [ObservableProperty]
    private bool isLoading;

    public BrowserViewModel(ISchemeLoader bookmarks, ISettingsService settings, StatusService status, ShellNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        navigator.BookmarksChanged += ReloadBookmarks;

        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        ReloadBookmarks();

        // Reopen where you left off, else the first bookmark that is a real place. The shipped
        // sample uses a reserved ".test" address, and it must never be where a new user lands.
        var last = _settings.Current.LastSchemeName;
        SetCurrent(Bookmarks.FirstOrDefault(b => string.Equals(b.Name, last, StringComparison.Ordinal))
                   ?? Bookmarks.FirstOrDefault(b => !SiteNaming.IsPlaceholderAddress(b.Address))
                   ?? Bookmarks.FirstOrDefault());
    }

    public ObservableCollection<BookmarkItemViewModel> Bookmarks { get; } = new();

    public bool HasBookmarks => Bookmarks.Count > 0;

    /// <summary>False when the selected bookmark has no search URL, which disables Search.</summary>
    public bool SelectedCanSearch => SelectedBookmark?.CanSearch == true;

    public string BookmarksFolder => _bookmarks.SchemesDirectory;

    public void Report(string message) => _status.Report(message);

    /// <summary>Re-reads bookmark files, keeping the selection when it still exists.</summary>
    public void ReloadBookmarks()
    {
        var previousAddress = SelectedBookmark?.Address;

        Bookmarks.Clear();
        foreach (var scheme in _bookmarks.LoadAll())
        {
            Bookmarks.Add(new BookmarkItemViewModel(scheme));
        }

        var restored = previousAddress is null
            ? null
            : Bookmarks.FirstOrDefault(b => string.Equals(b.Address, previousAddress, StringComparison.OrdinalIgnoreCase));

        SetCurrent(restored);
        OnPropertyChanged(nameof(HasBookmarks));
        OnPropertyChanged(nameof(SelectedCanSearch));
    }

    /// <summary>Marks which bookmark matches the page being viewed. Never navigates.</summary>
    public bool SelectForUrl(string? url)
    {
        var host = SiteNaming.NormalizedHost(url);
        if (host is null)
        {
            return false;
        }

        var match = Bookmarks.FirstOrDefault(b =>
            string.Equals(SiteNaming.NormalizedHost(b.Address), host, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return false;
        }

        SetCurrent(match);
        return true;
    }

    /// <summary>The bookmark whose address matches a URL, or null when the page is not saved yet.</summary>
    public BookmarkItemViewModel? FindBookmark(string? url)
    {
        var host = SiteNaming.NormalizedHost(url);
        if (host is null)
        {
            return null;
        }

        return Bookmarks.FirstOrDefault(b =>
            string.Equals(SiteNaming.NormalizedHost(b.Address), host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Selects a bookmark. Navigation is the caller's job, so this can never cause a redirect loop.</summary>
    public void SelectBookmark(BookmarkItemViewModel item)
    {
        SetCurrent(item);
        _settings.Update(s => s.LastSchemeName = item.Name);
    }

    /// <summary>
    /// Home opens the selected bookmark's own page, falling back to the current site's root
    /// when nothing is selected.
    /// </summary>
    public string? ResolveHomeUrl(string? currentUrl)
        => SelectedBookmark?.Address ?? SiteNaming.SiteRoot(currentUrl);

    /// <summary>Search URL for the selected bookmark, or null when it cannot search / no query typed.</summary>
    public string? BuildSearchUrl()
    {
        if (SelectedBookmark is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return null;
        }

        return SelectedBookmark.Model.BuildSearchUrl(SearchQuery.Trim());
    }

    public SiteScheme SaveBookmark(SiteScheme scheme, string? originalName = null)
    {
        var saved = _bookmarks.Save(scheme, originalName);
        ReloadBookmarks();
        SetCurrent(Bookmarks.FirstOrDefault(b => string.Equals(b.Name, saved.Name, StringComparison.Ordinal)));
        return saved;
    }

    public bool DeleteBookmark(SiteScheme scheme)
    {
        var deleted = _bookmarks.Delete(scheme);
        ReloadBookmarks();
        return deleted;
    }

    private void SetCurrent(BookmarkItemViewModel? item)
    {
        foreach (var bookmark in Bookmarks)
        {
            bookmark.IsCurrent = ReferenceEquals(bookmark, item);
        }

        SelectedBookmark = item;
        OnPropertyChanged(nameof(SelectedCanSearch));
    }
}
