using System.Windows;
using System.Windows.Controls;
using SevenSeas.Launcher.ViewModels;

namespace SevenSeas.Launcher.Views;

/// <summary>The Settings view.</summary>
public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel _viewModel;

    public SettingsView(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += (_, _) =>
        {
            _viewModel.LoadFromSettings();
            _viewModel.ReloadBookmarks();
        };
    }

    // Editing lives here, so there is exactly one place bookmarks are managed.

    private void OnAddBookmarkClick(object sender, RoutedEventArgs e)
    {
        var editor = SchemeEditorWindow.ForAdd();
        editor.Owner = Window.GetWindow(this);

        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            _viewModel.SaveBookmark(editor.Result, originalName: null);
        }
    }

    private void OnEditBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BookmarkItemViewModel item })
        {
            return;
        }

        var editor = SchemeEditorWindow.ForEdit(item.Model);
        editor.Owner = Window.GetWindow(this);

        if (editor.ShowDialog() == true && editor.Result is not null)
        {
            _viewModel.SaveBookmark(editor.Result, item.Name);
        }
    }

    private void OnRemoveBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BookmarkItemViewModel item })
        {
            return;
        }

        if (Dialogs.Confirm(
                "Remove bookmark",
                $"Remove '{item.Name}'?\n\nOnly the bookmark is removed; nothing on disk is touched.",
                "Remove",
                "Keep"))
        {
            _viewModel.DeleteBookmark(item.Model);
        }
    }
}
