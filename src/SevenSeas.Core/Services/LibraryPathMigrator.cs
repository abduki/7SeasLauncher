using SevenSeas.Core.Models;

namespace SevenSeas.Core.Services;

/// <summary>What has to happen to one game when the games folder moves.</summary>
public enum GameRelocationState
{
    /// <summary>The files are already at the new root; only the stored paths are stale.</summary>
    AlreadyAtNewRoot,

    /// <summary>The files are still under the old root and can be moved.</summary>
    StillAtOldRoot,

    /// <summary>Found in neither place, so the user has to point at it again.</summary>
    Missing,
}

/// <summary>
/// Where one game's files are, and where its stored paths should point after the move.
/// <paramref name="ExecutablePath"/> is null when the executable is not under the game folder,
/// in which case the caller has to pick one again.
/// </summary>
public sealed record GameRelocation(
    Game Game,
    GameRelocationState State,
    string InstallPath,
    string? ExecutablePath);

/// <summary>
/// Keeps the library pointing at its games when the games folder is changed.
///
/// A game row stores absolute paths, so moving the folder would otherwise leave every tile
/// pointing at a folder that no longer exists — the games would still be on disk, but Play would
/// fail. This works out, per game, whether the files are already at the new root, still need
/// moving, or have gone missing.
/// </summary>
public static class LibraryPathMigrator
{
    public static IReadOnlyList<GameRelocation> Plan(
        IReadOnlyList<Game> games,
        string? oldRoot,
        string? newRoot,
        Func<string, bool>? directoryExists = null)
    {
        ArgumentNullException.ThrowIfNull(games);
        directoryExists ??= Directory.Exists;

        var relocations = new List<GameRelocation>(games.Count);
        if (string.IsNullOrWhiteSpace(newRoot))
        {
            return relocations;
        }

        foreach (var game in games)
        {
            var leaf = LeafFolderName(game.InstallPath, oldRoot);

            // Games installed directly in the root have no folder to move.
            if (leaf is null)
            {
                continue;
            }

            var target = Path.Combine(newRoot, leaf);
            var state = directoryExists(target)
                ? GameRelocationState.AlreadyAtNewRoot
                : directoryExists(game.InstallPath)
                    ? GameRelocationState.StillAtOldRoot
                    : GameRelocationState.Missing;

            relocations.Add(new GameRelocation(game, state, target, ReRootExecutable(game, target)));
        }

        return relocations;
    }

    /// <summary>Writes the planned paths onto the game. Moving files is a separate step.</summary>
    public static void Apply(GameRelocation relocation)
    {
        ArgumentNullException.ThrowIfNull(relocation);

        relocation.Game.InstallPath = relocation.InstallPath;

        if (!string.IsNullOrWhiteSpace(relocation.ExecutablePath))
        {
            relocation.Game.ExecutablePath = relocation.ExecutablePath;
        }
    }

    /// <summary>Moves a game folder to the planned location, or reports why it could not.</summary>
    public static bool TryMove(GameRelocation relocation, out string error, Action<string, string>? move = null)
    {
        ArgumentNullException.ThrowIfNull(relocation);
        error = string.Empty;

        var source = relocation.Game.InstallPath;
        var destination = relocation.InstallPath;

        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Directory.Exists(source))
        {
            error = "The game folder is no longer where the library thinks it is.";
            return false;
        }

        if (Directory.Exists(destination))
        {
            error = "A folder already exists at the new location.";
            return false;
        }

        try
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            (move ?? MoveDirectory)(source, destination);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// <see cref="Directory.Move(string, string)"/> fails across volumes, which is exactly what
    /// happens when someone points the games folder at another drive, so fall back to a copy.
    /// </summary>
    public static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    /// <summary>The game's own folder name, or null when it is not inside the old root.</summary>
    private static string? LeafFolderName(string installPath, string? oldRoot)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(oldRoot))
        {
            try
            {
                if (string.Equals(
                        Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        var leaf = Path.GetFileName(installPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return string.IsNullOrWhiteSpace(leaf) ? null : leaf;
    }

    /// <summary>Maps the executable onto the new folder, keeping its position inside the game.</summary>
    private static string? ReRootExecutable(Game game, string newInstallPath)
    {
        if (string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            return null;
        }

        if (!PathSafety.IsWithin(game.InstallPath, game.ExecutablePath, out var executable))
        {
            return null;
        }

        var relative = Path.GetRelativePath(game.InstallPath, executable);
        return relative.StartsWith("..", StringComparison.Ordinal)
            ? null
            : Path.Combine(newInstallPath, relative);
    }
}
