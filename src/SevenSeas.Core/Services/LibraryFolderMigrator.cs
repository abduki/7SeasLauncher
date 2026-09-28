using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// Carries out a games-folder change.
///
/// Every game row stores absolute paths, so repointing the games folder without touching them
/// leaves the library aiming at a folder that no longer exists — the games are still on disk but
/// Play fails with no explanation. This decides, per game, whether the files are already at the new
/// root, still need moving, or have gone missing, and then rewrites the stored paths.
///
/// It never asks the user anything: the caller decides whether to move, because only the caller
/// knows how to put a question on screen.
/// </summary>
public sealed class LibraryFolderMigrator
{
    private readonly IGameRepository _games;
    private readonly IExecutableFinder _executableFinder;
    private readonly ILogger<LibraryFolderMigrator> _logger;

    public LibraryFolderMigrator(
        IGameRepository games,
        IExecutableFinder executableFinder,
        ILogger<LibraryFolderMigrator>? logger = null)
    {
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _executableFinder = executableFinder ?? throw new ArgumentNullException(nameof(executableFinder));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryFolderMigrator>.Instance;
    }

    /// <summary>What a change from <paramref name="oldRoot"/> to <paramref name="newRoot"/> would do.</summary>
    public IReadOnlyList<GameRelocation> Plan(string? oldRoot, string newRoot)
        => LibraryPathMigrator.Plan(_games.GetAllGames(), oldRoot, newRoot);

    /// <summary>How many games are still in the old folder and would need moving.</summary>
    public static int CountStranded(IReadOnlyList<GameRelocation> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var stranded = 0;
        foreach (var relocation in plan)
        {
            if (relocation.State == GameRelocationState.StillAtOldRoot)
            {
                stranded++;
            }
        }

        return stranded;
    }

    /// <summary>
    /// Applies the plan. When <paramref name="moveThem"/> is false the games left in the old folder
    /// keep their existing paths, so nothing is silently broken by a half-finished move.
    /// </summary>
    public string Commit(IReadOnlyList<GameRelocation> plan, bool moveThem)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Count == 0)
        {
            return string.Empty;
        }

        var moved = 0;
        var repointed = 0;
        var failed = 0;
        var missing = 0;
        var left = 0;

        foreach (var relocation in plan)
        {
            switch (relocation.State)
            {
                case GameRelocationState.Missing:
                    missing++;
                    continue;

                case GameRelocationState.StillAtOldRoot:
                    if (!moveThem)
                    {
                        left++;
                        continue;
                    }

                    if (!LibraryPathMigrator.TryMove(relocation, out var error))
                    {
                        _logger.LogWarning("Could not move '{Title}': {Error}", relocation.Game.Title, error);
                        failed++;
                        continue;
                    }

                    moved++;
                    break;

                default:
                    repointed++;
                    break;
            }

            LibraryPathMigrator.Apply(relocation);
            PointAtAnExecutableAgain(relocation.Game);
            _games.Update(relocation.Game);
        }

        var parts = new List<string>();
        if (moved > 0) parts.Add(moved + " game folder(s) moved");
        if (repointed > 0) parts.Add(repointed + " game(s) repointed");
        if (left > 0) parts.Add(left + " left in the old folder");
        if (failed > 0) parts.Add(failed + " could not be moved");
        if (missing > 0) parts.Add(missing + " missing and skipped");

        var summary = parts.Count == 0
            ? string.Empty
            : "Games folder updated: " + string.Join(", ", parts) + ".";

        if (summary.Length > 0)
        {
            _logger.LogInformation("{Summary}", summary);
        }

        return summary;
    }

    /// <summary>
    /// A moved game's executable can end up somewhere the plan could not predict, so try to find
    /// one again rather than leaving a Play button that fails without saying why.
    /// </summary>
    private void PointAtAnExecutableAgain(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath))
        {
            return;
        }

        var found = _executableFinder.Find(game.InstallPath);
        if (found.Found && !string.IsNullOrWhiteSpace(found.ExecutablePath))
        {
            game.ExecutablePath = found.ExecutablePath;
            return;
        }

        _logger.LogInformation("No executable found under '{Path}' after the move.", game.InstallPath);
        game.ExecutablePath = string.Empty;
    }
}
