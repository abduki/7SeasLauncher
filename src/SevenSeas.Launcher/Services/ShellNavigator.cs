namespace SevenSeas.Launcher.Services;

/// <summary>
/// Lets a view ask the Shell to switch tabs, so an empty state can offer a real "go there"
/// action instead of instructing the user in prose.
/// Named ShellNavigator rather than NavigationService to avoid colliding with WPF's own type.
/// </summary>
public sealed class ShellNavigator
{
    public const string BrowseSection = "Browse";
    public const string LibrarySection = "Library";
    public const string JobsSection = "Jobs";
    public const string SettingsSection = "Settings";

    public event Action<string>? SectionRequested;

    /// <summary>Raised when a secondary window wants the main browser to open a URL.</summary>
    public event Action<string>? RequestUrl;

    public void GoTo(string section) => SectionRequested?.Invoke(section);

    public void OpenUrlInMainWindow(string url) => RequestUrl?.Invoke(url);

    /// <summary>Raised when bookmarks changed somewhere other than the main window.</summary>
    public event Action? BookmarksChanged;

    public void NotifyBookmarksChanged() => BookmarksChanged?.Invoke();
}
