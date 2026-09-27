using CommunityToolkit.Mvvm.ComponentModel;
using SevenSeas.Core.Models;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>One button on the bookmarks bar.</summary>
public partial class BookmarkItemViewModel : ObservableObject
{
    public BookmarkItemViewModel(SiteScheme model)
        => Model = model ?? throw new ArgumentNullException(nameof(model));

    public SiteScheme Model { get; }

    public string Name => Model.Name;

    public string Address => Model.BaseUrl;

    /// <summary>Searchable bookmarks can be searched; browsable-only ones cannot yet.</summary>
    public bool CanSearch => Model.CanSearch;

    /// <summary>Shown in the Settings list.</summary>
    public string SearchabilityLabel => CanSearch ? "Searchable" : "Browse only";

    public string Tooltip => CanSearch
        ? $"{Address}{Environment.NewLine}Click to open. This bookmark can be searched."
        : $"{Address}{Environment.NewLine}Click to open. No search URL yet — use Save this page to add one.";

    /// <summary>True for the bookmark matching the page being viewed.</summary>
    [ObservableProperty]
    private bool isCurrent;
}
