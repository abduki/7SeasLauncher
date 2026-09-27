using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>What <see cref="ExtractionNormalizer"/> changed about an extracted folder.</summary>
public sealed record ExtractionNormalizationResult(bool Flattened, int FixFoldersMerged, string Root);

/// <summary>
/// Tidies up what an archive actually extracted to.
/// <para>
/// Two shapes are common and both need handling before a game is usable:
/// a single wrapper folder (<c>Title\Title\game.exe</c>), and the game plus a separate
/// "Fix"/"Repair"/"Crack" folder whose contents must be copied over the game.
/// </para>
/// </summary>
public static class ExtractionNormalizer
{
    private static readonly string[] FixKeywords =
    {
        "fix", "repair", "crack", "codex", "plaza", "skidrow", "goldberg", "reloaded", "rld",
        "patch", "update", "keygen", "emu", "steamemu", "onlinefix", "unlocker",
    };

    /// <summary>True when a folder name looks like a fix/repair/crack payload rather than the game itself.</summary>
    public static bool LooksLikeFixFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var name = new string(folderName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var keyword in FixKeywords)
        {
            if (name.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Unwraps a single wrapper folder, then merges any fix folders into the game folder.</summary>
    public static ExtractionNormalizationResult Normalize(string root, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new ExtractionNormalizationResult(false, 0, root ?? string.Empty);
        }

        var current = Path.GetFullPath(root);
        var flattened = false;

        // 1) TitleTitle... -> Title...
        for (var depth = 0; depth < 4; depth++)
        {
            var entries = Directory.GetFileSystemEntries(current);
            if (entries.Length != 1 || !Directory.Exists(entries[0]))
            {
                break;
            }

            var innerName = Path.GetFileName(entries[0]);
            if (LooksLikeFixFolder(innerName))
            {
                break;
            }

            MoveContents(entries[0], current, logger);
            TryDeleteDirectory(entries[0], logger);
            flattened = true;
        }

        // 2) Game + Fix/Repair: copy the fix over the game, then drop the fix folder.
        var merged = 0;
        var directories = Directory.GetDirectories(current);
        if (directories.Length > 0)
        {
            var fixFolders = directories.Where(d => LooksLikeFixFolder(Path.GetFileName(d))).ToList();

            if (fixFolders.Count > 0)
            {
                var gameFolders = directories
                    .Where(d => !LooksLikeFixFolder(Path.GetFileName(d)))
                    .OrderByDescending(CountFiles)
                    .ToList();

                var target = gameFolders.FirstOrDefault() ?? current;

                foreach (var fix in fixFolders)
                {
                    if (string.Equals(Path.GetFullPath(fix), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    logger?.LogInformation("Merging fix folder {Fix} into {Target}.", Path.GetFileName(fix), Path.GetFileName(target));
                    MoveContents(fix, target, logger);
                    TryDeleteDirectory(fix, logger);
                    merged++;
                }
            }
        }

        return new ExtractionNormalizationResult(flattened, merged, current);
    }

    private static int CountFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Copies everything from source into destination, overwriting, then removes the source.</summary>
    private static void MoveContents(string source, string destination, ILogger? logger)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var parent = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            try
            {
                File.Copy(file, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Could not merge {File}.", relative);
            }
        }
    }

    private static void TryDeleteDirectory(string path, ILogger? logger)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Could not remove {Path} after merging.", path);
        }
    }
}
