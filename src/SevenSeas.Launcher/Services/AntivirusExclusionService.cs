using System.Diagnostics;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Services;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Launcher.Services;

/// <summary>
/// Helps the user exclude 7SeasLauncher's folders from Windows Security, which is the usual fix for
/// downloads that "complete" and then vanish.
/// </summary>
public sealed class AntivirusExclusionService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<AntivirusExclusionService> _logger;

    public AntivirusExclusionService(ISettingsService settings, ILogger<AntivirusExclusionService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AntivirusExclusionService>.Instance;
    }

    /// <summary>Normally a single folder: the one holding Games, Temp and Trash.</summary>
    public IReadOnlyList<string> RecommendedFolders()
        => AntivirusExclusions.RecommendedFolders(_settings.Current);

    /// <summary>Opens the Windows Security page that has the exclusions list.</summary>
    public bool OpenWindowsSecurity()
    {
        foreach (var target in new[] { "windowsdefender://threatsettings", "windowsdefender://threat" })
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not open {Target}.", target);
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the exclusions with an elevated PowerShell. Windows shows a UAC prompt, which is the
    /// consent step; nothing is changed silently.
    /// </summary>
    public bool TryAddExclusions(out string message)
    {
        var folders = RecommendedFolders();
        if (folders.Count == 0)
        {
            message = "7SeasLauncher's folders are not configured yet.";
            return false;
        }

        try
        {
            var lines = new List<string> { "$ErrorActionPreference = 'Stop'", "try {" };
            foreach (var folder in folders)
            {
                lines.Add("  Add-MpPreference -ExclusionPath '" + folder.Replace("'", "''") + "'");
            }

            lines.Add("  Add-MpPreference -ExclusionProcess '" + AntivirusExclusions.LauncherExecutableName + "'");
            lines.Add("  'Added: " + string.Join("; ", folders).Replace("'", "''") + "'");
            lines.Add("} catch { Write-Host ('Could not add the exclusion: ' + $_.Exception.Message) }");
            lines.Add("Start-Sleep -Seconds 4");

            var scriptPath = Path.Combine(Path.GetTempPath(), "7seas-add-exclusions.ps1");
            File.WriteAllLines(scriptPath, lines);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                UseShellExecute = true,
                Verb = "runas",
            });

            _logger.LogInformation("Requested antivirus exclusions for {Folders}.", string.Join(", ", folders));
            message = "Approve the Windows prompt to add the exclusions.";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not add antivirus exclusions.");
            message = "Could not add the exclusion automatically: " + ex.Message;
            return false;
        }
    }
}
