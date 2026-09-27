using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SevenSeas.Launcher.Services;
using SevenSeas.Launcher.ViewModels;

namespace SevenSeas.Launcher.Views;

/// <summary>The library grid, read-only against the Library Repo.</summary>
public partial class LibraryView : UserControl
{
    private readonly LibraryViewModel _viewModel;
    private readonly ShellNavigator _navigator;

    public LibraryView(LibraryViewModel viewModel, ShellNavigator navigator)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));

        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.IsEmpty))
            {
                UpdateEmptyState();
            }
        };

        Loaded += (_, _) =>
        {
            _viewModel.Refresh();
            UpdateEmptyState();
        };
    }

    private void UpdateEmptyState()
        => EmptyState.Visibility = _viewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Reuses the tile's own context menu so right-click and the More button show one list.
    /// </summary>
    private void OnTileMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var tile = FindAncestor<Border>(element);
        if (tile?.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = sender as UIElement;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Refresh();
        UpdateEmptyState();
    }

    private void OnGoToBrowseClick(object sender, RoutedEventArgs e)
        => _navigator.GoTo(ShellNavigator.BrowseSection);
}
