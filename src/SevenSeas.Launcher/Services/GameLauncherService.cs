using System.Diagnostics;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Launcher.Services;

/// <summary>Launch / reveal / uninstall operations for the Library View.</summary>
public sealed class GameLauncherService
{
    private readonly IGameRepository _games;
    private readonly ILogger<GameLauncherService> _logger;

    public GameLauncherService(IGameRepository games, ILogger<GameLauncherService>? logger = null)
    {
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GameLauncherService>.Instance;
    }

    public bool Launch(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath))
        {
            _logger.LogWarning("Executable for '{Title}' is missing: {Path}", game.Title, game.ExecutablePath);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = game.ExecutablePath,
                WorkingDirectory = game.InstallPath,
                UseShellExecute = true,
            });
            _logger.LogInformation("Launched '{Title}'.", game.Title);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch '{Title}'.", game.Title);
            return false;
        }
    }

    public void OpenInstallFolder(Game game)
    {
        if (Directory.Exists(game.InstallPath))
        {
            Process.Start(new ProcessStartInfo { FileName = game.InstallPath, UseShellExecute = true });
        }
    }

    public void OpenPath(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }

    /// <summary>Removes a game from the library and optionally deletes its files.</summary>
    public (bool Removed, bool FilesDeleted, string Message) Uninstall(Game game, bool deleteFiles)
    {
        ArgumentNullException.ThrowIfNull(game);

        var filesDeleted = false;
        if (deleteFiles && Directory.Exists(game.InstallPath))
        {
            try
            {
                Directory.Delete(game.InstallPath, recursive: true);
                filesDeleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete {Path}.", game.InstallPath);
                return (false, false, "The game is still running or its files are locked, so nothing was removed.");
            }
        }

        _games.DeleteGame(game.Id);
        _logger.LogInformation("Uninstalled '{Title}' (files deleted: {Deleted}).", game.Title, filesDeleted);
        return (true, filesDeleted, filesDeleted
            ? $"'{game.Title}' and its files were removed."
            : $"'{game.Title}' was removed from the library; its files were kept.");
    }
}
