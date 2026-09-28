using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Launcher.Services;
using SevenSeas.Launcher.Views;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>The library grid, fed exclusively by the Library Repository.</summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly IGameRepository _games;
    private readonly GameLauncherService _launcher;
    private readonly IMetadataService _metadata;
    private readonly IExecutableFinder _executableFinder;
    private readonly ISettingsService _settings;
    private readonly StatusService _status;

    [ObservableProperty]
    private bool isEmpty = true;

    public LibraryViewModel(
        IGameRepository games,
        GameLauncherService launcher,
        IMetadataService metadata,
        IExecutableFinder executableFinder,
        ISettingsService settings,
        StatusService status)
    {
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _executableFinder = executableFinder ?? throw new ArgumentNullException(nameof(executableFinder));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        // The pipeline raises GameAdded from its own thread, and WPF collections may only be changed
        // on the dispatcher — otherwise the exception propagates back and fails an otherwise good job.
        _games.GameAdded += _ => Refresh();

        // Changing the games folder rewrites every stored path, so the grid has to follow.
        _settings.SettingsChanged += _ => Refresh();

        Refresh();
    }

    public ObservableCollection<GameItemViewModel> Games { get; } = new();

    public void Refresh() => OnUiThread(() =>
    {
        var installed = _games.GetAllGames();

        Games.Clear();
        foreach (var game in installed)
        {
            Games.Add(new GameItemViewModel(game, _launcher, _games, _metadata, _executableFinder, Refresh));
        }

        IsEmpty = Games.Count == 0;
    });

    /// <summary>
    /// Re-reads the library and re-queries SteamGridDB for every game's cover art.
    ///
    /// Lookups run one at a time with a pause between them: the API is rate limited and a burst
    /// would get the key blocked. Games whose art has not changed are left alone so a refresh does
    /// not rewrite rows for nothing.
    /// </summary>
    public async Task RefreshCoverArtAsync()
    {
        Refresh();

        var games = _games.GetAllGames();
        if (games.Count == 0)
        {
            _status.Report("The library is empty.");
            return;
        }

        _status.Report($"Refreshing cover art for {games.Count} game(s)\u2026");

        var updated = 0;
        var missed = 0;

        foreach (var game in games)
        {
            try
            {
                var metadata = await _metadata.LookupAsync(game.Title);

                // Deliberately not adopting the returned title: a refresh is not a rename.
                if (metadata.Matched && !string.Equals(game.CoverUrl, metadata.CoverUrl, StringComparison.Ordinal))
                {
                    game.CoverUrl = metadata.CoverUrl;
                    _games.Update(game);
                    updated++;
                }
                else if (!metadata.Matched)
                {
                    missed++;
                }
            }
            catch (Exception)
            {
                // Metadata is a nicety; a failed lookup must not abort the whole refresh.
                missed++;
            }

            await Task.Delay(250);
        }

        Refresh();

        _status.Report(missed == 0
            ? $"Cover art refreshed for {updated} game(s)."
            : $"Cover art refreshed for {updated} game(s); {missed} had no match.");
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}

/// <summary>One cover-art tile in the library grid.</summary>
public partial class GameItemViewModel : ObservableObject
{
    private readonly Game _game;
    private readonly GameLauncherService _launcher;
    private readonly IGameRepository _games;
    private readonly IMetadataService _metadata;
    private readonly IExecutableFinder _executableFinder;
    private readonly Action _refreshLibrary;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private System.Windows.Media.ImageSource? coverImage;

    public GameItemViewModel(
        Game game,
        GameLauncherService launcher,
        IGameRepository games,
        IMetadataService metadata,
        IExecutableFinder executableFinder,
        Action refreshLibrary)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _executableFinder = executableFinder ?? throw new ArgumentNullException(nameof(executableFinder));
        _refreshLibrary = refreshLibrary ?? throw new ArgumentNullException(nameof(refreshLibrary));
        CoverImage = TryCreateImage(game.CoverUrl);
    }

    /// <summary>Correct the title, or point the game at a different executable.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task EditAsync()
    {
        var candidates = _executableFinder.ListCandidates(_game.InstallPath);

        var dialog = new Views.GameEditorDialog(_game.Title, _game.ExecutablePath, candidates);
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
        {
            dialog.Owner = owner;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var titleChanged = !string.Equals(dialog.GameTitle, _game.Title, StringComparison.Ordinal);

        _game.Title = dialog.GameTitle;
        _game.ExecutablePath = dialog.ExecutablePath;

        // Renaming is usually a correction, so look the artwork up again under the new name.
        if (titleChanged)
        {
            StatusMessage = "Looking up cover art…";

            try
            {
                var metadata = await _metadata.LookupAsync(_game.Title);
                if (metadata.Matched)
                {
                    _game.CoverUrl = metadata.CoverUrl;
                    _game.Title = metadata.Title;
                }
            }
            catch (Exception)
            {
                // Metadata is a nicety; the rename still stands.
            }
        }

        _games.Update(_game);
        _refreshLibrary();
    }

    public string Id => _game.Id;

    public string Title => _game.Title;

    public string InstallPath => _game.InstallPath;

    public string ExecutablePath => _game.ExecutablePath;

    public string Initial => string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim()[..1].ToUpperInvariant();

    public bool HasCover => CoverImage is not null;

    private static System.Windows.Media.ImageSource? TryCreateImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnDemand;
            image.DecodePixelWidth = 220;
            image.EndInit();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Launch()
    {
        if (!_launcher.Launch(_game))
        {
            StatusMessage = "Could not launch the game; its executable is missing.";
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenFolder() => _launcher.OpenInstallFolder(_game);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Uninstall()
    {
        var choice = Dialogs.Choose(
            "Uninstall game",
            $"Remove '{Title}'?\n\nDelete the files as well, or just take it out of the library?",
            "Remove and delete files",
            "Remove, keep files");

        if (choice < 0)
        {
            return;
        }

        var result = _launcher.Uninstall(_game, deleteFiles: choice == 0);
        StatusMessage = result.Message;
    }
}
