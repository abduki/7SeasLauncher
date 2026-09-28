using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;
using SevenSeas.Launcher.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>The Settings view; writes through the Settings Service.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IMetadataService _metadata;
    private readonly ISchemeLoader _bookmarkStore;
    private readonly DiagnosticsExporter _diagnostics;
    private readonly GameLauncherService _launcher;
    private readonly AntivirusExclusionService _antivirus;
    private readonly SpaceCleanupService _cleanup;
    private readonly LibraryFolderMigrator _migrator;
    private readonly StatusService _status;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string gamesFolder = string.Empty;
    [ObservableProperty] private string tempFolder = string.Empty;
    [ObservableProperty] private string trashFolder = string.Empty;
    [ObservableProperty] private string steamGridDbApiKey = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int selectedSectionIndex;

    /// <summary>Which nav rail section is showing.</summary>
    public bool ShowFolders => SelectedSectionIndex == 0;
    public bool ShowMetadata => SelectedSectionIndex == 1;
    public bool ShowBookmarks => SelectedSectionIndex == 2;
    public bool ShowAdvanced => SelectedSectionIndex == 3;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowFolders));
        OnPropertyChanged(nameof(ShowMetadata));
        OnPropertyChanged(nameof(ShowBookmarks));
        OnPropertyChanged(nameof(ShowAdvanced));
    }

    public SettingsViewModel(
        ISettingsService settings,
        IMetadataService metadata,
        ISchemeLoader bookmarks,
        DiagnosticsExporter diagnostics,
        GameLauncherService launcher,
        AntivirusExclusionService antivirus,
        SpaceCleanupService cleanup,
        LibraryFolderMigrator migrator,
        StatusService status,
        ILogger<SettingsViewModel>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _bookmarkStore = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _antivirus = antivirus ?? throw new ArgumentNullException(nameof(antivirus));
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsViewModel>.Instance;

        LoadFromSettings();
        ReloadBookmarks();
    }

    /// <summary>Every bookmark, searchable or not — one list, not two.</summary>
    public ObservableCollection<BookmarkItemViewModel> Bookmarks { get; } = new();

    public void LoadFromSettings()
    {
        var current = _settings.Current;
        GamesFolder = current.GamesFolder;
        TempFolder = current.TempFolder;
        TrashFolder = current.TrashFolder;
        SteamGridDbApiKey = current.SteamGridDbApiKey;
    }

    public void ReloadBookmarks()
    {
        Bookmarks.Clear();
        foreach (var scheme in _bookmarkStore.LoadAll())
        {
            Bookmarks.Add(new BookmarkItemViewModel(scheme));
        }
    }

    /// <summary>Writes a bookmark straight through, so Settings and the bar never diverge.</summary>
    public void SaveBookmark(SiteScheme scheme, string? originalName)
    {
        var saved = _bookmarkStore.Save(scheme, originalName);
        ReloadBookmarks();
        _status.Report(saved.CanSearch
            ? $"Saved bookmark '{saved.Name}'. It can be searched."
            : $"Saved bookmark '{saved.Name}'. Add a search URL to search it.");
    }

    public void DeleteBookmark(SiteScheme scheme)
    {
        if (_bookmarkStore.Delete(scheme))
        {
            ReloadBookmarks();
            _status.Report($"Removed bookmark '{scheme.Name}'.");
        }
        else
        {
            _status.Report($"Could not remove '{scheme.Name}'.");
        }
    }

    [RelayCommand]
    private void Save()
    {
        var previousGamesFolder = _settings.Current.GamesFolder;
        var newGamesFolder = GamesFolder.Trim();

        // Repointing the games folder rewrites every stored path, so settle what happens to the
        // installed games before saving. Cancelling here has to leave the setting untouched.
        var plan = _migrator.Plan(previousGamesFolder, newGamesFolder);
        var stranded = LibraryFolderMigrator.CountStranded(plan);
        var moveThem = false;

        if (stranded > 0)
        {
            var choice = Dialogs.Choose(
                "Games folder changed",
                $"{stranded} installed game(s) are still in the old folder:\n\n{previousGamesFolder}\n\n" +
                $"Move them to the new folder?\n\n{newGamesFolder}",
                "Move them now",
                "Leave them where they are",
                "Cancel the change");

            if (choice < 0)
            {
                GamesFolder = previousGamesFolder;
                _status.Report("Folder change cancelled; the games folders are unchanged.");
                return;
            }

            moveThem = choice == 0;
        }

        var migration = _migrator.Commit(plan, moveThem);

        _settings.Update(s =>
        {
            s.GamesFolder = newGamesFolder;
            s.TempFolder = TempFolder.Trim();
            s.TrashFolder = TrashFolder.Trim();
            s.SteamGridDbApiKey = SteamGridDbApiKey.Trim();
        });

        _status.Report(string.IsNullOrEmpty(migration) ? "Settings saved." : migration);
    }

    [RelayCommand]
    private void BrowseGamesFolder() => GamesFolder = PickFolder(GamesFolder);

    [RelayCommand]
    private void BrowseTempFolder() => TempFolder = PickFolder(TempFolder);

    [RelayCommand]
    private void BrowseTrashFolder() => TrashFolder = PickFolder(TrashFolder);

    /// <summary>Makes one real SteamGridDB call so the user knows the key works.</summary>
    [RelayCommand]
    private async Task TestApiKeyAsync()
    {
        Save();
        IsBusy = true;
        _status.Report("Contacting SteamGridDB\u2026");

        try
        {
            var result = await _metadata.LookupAsync("Hollow Knight");

            _status.Report(string.IsNullOrWhiteSpace(result.CoverUrl)
                ? $"SteamGridDB replied for '{result.Title}' but returned no cover art. Check the key is valid and not rate-limited."
                : $"SteamGridDB OK \u2014 matched '{result.Title}' and found cover art.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API key test failed.");
            _status.Report($"API test failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSchemesFolder()
    {
        Directory.CreateDirectory(_bookmarkStore.SchemesDirectory);
        _launcher.OpenPath(_bookmarkStore.SchemesDirectory);
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);
        _launcher.OpenPath(AppPaths.LogsFolder);
    }

    [RelayCommand]
    private void ShowAntivirusExclusions()
    {
        var dialog = new AntivirusExclusionDialog(_antivirus);
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    /// <summary>Deletes the archives 7SeasLauncher has finished with, so old downloads stop filling the disk.</summary>
    [RelayCommand]
    private void FreeUpSpace()
    {
        var reclaimable = _cleanup.ReclaimableBytes();
        if (reclaimable == 0)
        {
            _status.Report("Nothing to free up — no leftover archives.");
            return;
        }

        if (!Dialogs.Confirm(
                "Free up space",
                $"Delete the archives 7SeasLauncher has finished with?\n\n" +
                $"This frees about {SpaceCleanupResult.Format(reclaimable)} from Trash and Temp. " +
                "Games you have already installed are not touched.",
                "Delete them",
                "Cancel"))
        {
            return;
        }

        _status.Report(_cleanup.CleanEverything().Describe());
    }

    [RelayCommand]
    private void ExportDiagnostics()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export diagnostics",
            FileName = $"7seas-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Filter = "Zip archive (*.zip)|*.zip",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var path = _diagnostics.Export(dialog.FileName);
            _status.Report($"Diagnostics exported to {path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostics export failed.");
            _status.Report($"Export failed: {ex.Message}");
        }
    }

    private static string PickFolder(string current)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder" };
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : current;
    }
}
