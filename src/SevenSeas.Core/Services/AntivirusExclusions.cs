using SevenSeas.Core.Models;

namespace SevenSeas.Core.Services;

/// <summary>
/// Works out which folders the user should exclude from their antivirus.
/// <para>
/// One exclusion should be enough: in the default layout Games, Temp and Trash all sit under a single
/// 7SeasLauncher folder, so recommending that parent keeps the instructions to a single choice.
/// </para>
/// </summary>
public static class AntivirusExclusions
{
    /// <summary>
    /// The folders to exclude, most preferred first. Normally exactly one: the folder containing
    /// Games, Temp and Trash.
    /// </summary>
    public static IReadOnlyList<string> RecommendedFolders(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var configured = new List<string>();
        AddIfUsable(configured, settings.GamesFolder);
        AddIfUsable(configured, settings.TempFolder);
        AddIfUsable(configured, settings.TrashFolder);

        if (configured.Count == 0)
        {
            return Array.Empty<string>();
        }

        var parent = Path.GetDirectoryName(configured[0]);
        if (parent is not null)
        {
            var allShareParent = true;
            foreach (var folder in configured)
            {
                if (!string.Equals(Path.GetDirectoryName(folder), parent, StringComparison.OrdinalIgnoreCase))
                {
                    allShareParent = false;
                    break;
                }
            }

            if (allShareParent)
            {
                return new[] { parent };
            }
        }

        return configured;
    }

    /// <summary>
    /// The process that should also be excluded, so the antivirus leaves the app's own file handling alone.
    /// </summary>
    public static string LauncherExecutableName => "7SeasLauncher.exe";

    private static void AddIfUsable(List<string> folders, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!folders.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(full);
        }
    }
}
