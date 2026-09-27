using System.ComponentModel;
using System.Windows;
using SevenSeas.Core.Abstractions;
using SevenSeas.Launcher.Services;
using SevenSeas.Launcher.ViewModels;
using SevenSeas.Launcher.Views;
using Wpf.Ui.Controls;

namespace SevenSeas.Launcher;

/// <summary>
/// The shell. Hosts the user-facing views and the status bar, and persists window geometry to
/// settings.json.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ISettingsService _settings;

    public MainWindow(
        MainViewModel viewModel,
        ISettingsService settings,
        ShellNavigator navigator,
        BrowserView browserView,
        LibraryView libraryView,
        JobsView jobsView,
        SettingsView settingsView)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(navigator);

        InitializeComponent();

        DataContext = viewModel;
        BrowseTab.Content = browserView;
        LibraryTab.Content = libraryView;
        JobsTab.Content = jobsView;
        SettingsTab.Content = settingsView;

        // Empty states can send the user somewhere useful.
        navigator.SectionRequested += section => MainTabs.SelectedIndex = section switch
        {
            ShellNavigator.LibrarySection => 1,
            ShellNavigator.JobsSection => 2,
            ShellNavigator.SettingsSection => 3,
            _ => 0,
        };

        RestoreWindowState();
    }

    private void RestoreWindowState()
    {
        var state = _settings.Current.WindowState;
        if (state.Width > 200)
        {
            Width = state.Width;
        }

        if (state.Height > 200)
        {
            Height = state.Height;
        }

        if (!double.IsNaN(state.Left) && !double.IsNaN(state.Top) &&
            state.Left > -10000 && state.Top > -10000)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = state.Left;
            Top = state.Top;
        }

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        var bounds = RestoreBounds;
        var isMaximized = WindowState == WindowState.Maximized;

        _settings.Update(s =>
        {
            s.WindowState.IsMaximized = isMaximized;
            if (!isMaximized && bounds.Width > 200 && bounds.Height > 200)
            {
                s.WindowState.Width = bounds.Width;
                s.WindowState.Height = bounds.Height;
                s.WindowState.Left = bounds.Left;
                s.WindowState.Top = bounds.Top;
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        try
        {
            SaveWindowState();
        }
        catch (Exception)
        {
            // Never block shutdown on a settings write.
        }

        base.OnClosing(e);
    }
}
