using System.IO.Compression;
using SevenSeas.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Launcher.Services;

/// <summary>Settings → Export Diagnostics: zips the logs plus the settings file with its API key redacted.</summary>
public sealed class DiagnosticsExporter
{
    private readonly ISettingsService _settings;
    private readonly ILogger<DiagnosticsExporter> _logger;

    public DiagnosticsExporter(ISettingsService settings, ILogger<DiagnosticsExporter>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DiagnosticsExporter>.Instance;
    }

    public string Export(string targetZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        var staging = Path.Combine(Path.GetTempPath(), "7seas-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var logsTarget = Path.Combine(staging, "logs");
            if (Directory.Exists(AppPaths.LogsFolder))
            {
                CopyDirectory(AppPaths.LogsFolder, logsTarget);
            }

            var redacted = _settings.Current.Clone();
            redacted.SteamGridDbApiKey = string.IsNullOrWhiteSpace(redacted.SteamGridDbApiKey)
                ? string.Empty
                : "<redacted>";
            File.WriteAllText(
                Path.Combine(staging, "settings.redacted.json"),
                System.Text.Json.JsonSerializer.Serialize(redacted, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                }));

            File.WriteAllText(
                Path.Combine(staging, "environment.txt"),
                $"Generated: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $".NET: {Environment.Version}{Environment.NewLine}" +
                $"Machine: {Environment.MachineName}{Environment.NewLine}");

            var directory = Path.GetDirectoryName(targetZipPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(targetZipPath))
            {
                File.Delete(targetZipPath);
            }

            ZipFile.CreateFromDirectory(staging, targetZipPath);
            _logger.LogInformation("Diagnostics exported to {Path}.", targetZipPath);
            return targetZipPath;
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }
}
