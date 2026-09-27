using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// Scores .exe files and picks the launcher.
/// Filters installer/CRASH-handler noise, prefers the executable named after the game and sitting in the root.
/// </summary>
public sealed class ExecutableFinder : IExecutableFinder
{
    private static readonly string[] NoiseKeywords =
    {
        "unins", "uninstall", "unitycrashhandler", "crashhandler", "crashpad", "crashreport",
        "vcredist", "redist", "dxsetup", "dxwebsetup", "directx", "setup", "installer",
        "update", "updater", "dotnet", "oalinst", "register", "activation", "helper",
        "eula", "readme", "report", "diagnostics",
    };

    private const int MaxDepth = 5;
    private const int ClearWinnerMargin = 12;

    private readonly ILogger<ExecutableFinder> _logger;

    public ExecutableFinder(ILogger<ExecutableFinder>? logger = null)
        => _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ExecutableFinder>.Instance;

    public ExecutableSearchResult Find(string installFolder)
    {
        if (string.IsNullOrWhiteSpace(installFolder) || !Directory.Exists(installFolder))
        {
            return ExecutableSearchResult.None(Array.Empty<ExecutableCandidate>());
        }

        var root = Path.GetFullPath(installFolder);
        var folderName = Normalize(new DirectoryInfo(root).Name);

        var candidates = new List<ExecutableCandidate>();
        foreach (var file in EnumerateExecutables(root))
        {
            var score = Score(file, root, folderName, out var size);
            candidates.Add(new ExecutableCandidate(file, score, size));
        }

        var ordered = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.SizeBytes)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            _logger.LogWarning("No .exe files found under {Folder}.", root);
            return ExecutableSearchResult.None(ordered);
        }

        var best = ordered[0];
        if (best.Score <= 0)
        {
            _logger.LogWarning("All {Count} executable candidates were filtered as noise under {Folder}.", ordered.Count, root);
            return ExecutableSearchResult.None(ordered);
        }

        var clearWinner = ordered.Count == 1 || best.Score - ordered[1].Score >= ClearWinnerMargin;
        if (!clearWinner)
        {
            _logger.LogInformation("Executable choice is ambiguous under {Folder}; asking the user.", root);
            return ExecutableSearchResult.Ambiguous(ordered);
        }

        _logger.LogInformation("Picked executable {Path} (score {Score}).", best.Path, best.Score);
        return ExecutableSearchResult.Chosen(best.Path, ordered);
    }

    public IReadOnlyList<ExecutableCandidate> ListCandidates(string installFolder)
    {
        if (string.IsNullOrWhiteSpace(installFolder) || !Directory.Exists(installFolder))
        {
            return Array.Empty<ExecutableCandidate>();
        }

        var root = Path.GetFullPath(installFolder);
        var folderName = Normalize(new DirectoryInfo(root).Name);

        return EnumerateExecutables(root)
            .Select(file => new ExecutableCandidate(file, Score(file, root, folderName, out var size), size))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.SizeBytes)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateExecutables(string root)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth > MaxDepth)
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in subdirectories)
            {
                queue.Enqueue((directory, depth + 1));
            }
        }
    }

    private static int Score(string file, string root, string folderName, out long size)
    {
        size = 0;
        try
        {
            size = new FileInfo(file).Length;
        }
        catch (IOException)
        {
            // size stays 0
        }

        var name = Path.GetFileNameWithoutExtension(file);
        var normalizedName = Normalize(name);
        var relative = Path.GetRelativePath(root, file);
        var depth = relative.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

        if (NoiseKeywords.Any(k => normalizedName.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return -1000;
        }

        var score = 10;

        if (normalizedName.Length > 0 && normalizedName == folderName)
        {
            score += 50;
        }
        else if (folderName.Length > 0 &&
                 (normalizedName.Contains(folderName, StringComparison.OrdinalIgnoreCase) ||
                  folderName.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) && normalizedName.Length >= 4))
        {
            score += 30;
        }

        score -= depth * 8;

        if (size >= 1_048_576)
        {
            score += 20;
        }
        else if (size >= 102_400)
        {
            score += 10;
        }
        else if (size < 4096)
        {
            score -= 15;
        }

        if (normalizedName.StartsWith("game", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.EndsWith("game", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        return score;
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
