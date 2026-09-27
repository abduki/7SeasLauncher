using SevenSeas.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>What a cleanup pass removed.</summary>
public sealed record SpaceCleanupResult(int FilesDeleted, long BytesFreed)
{
    public string Describe()
    {
        if (FilesDeleted == 0)
        {
            return "Nothing to clean up.";
        }

        return $"Removed {FilesDeleted} file(s), freeing {Format(BytesFreed)}.";
    }

    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// Reclaims disk space from the archive folders 7SeasLauncher keeps: the Trash it retains after a job and
/// any downloads left behind in Temp.
/// </summary>
public sealed class SpaceCleanupService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<SpaceCleanupService> _logger;

    public SpaceCleanupService(ISettingsService settings, ILogger<SpaceCleanupService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SpaceCleanupService>.Instance;
    }

    /// <summary>Bytes that would be freed if everything cleanable were removed.</summary>
    public long ReclaimableBytes()
        => Measure(_settings.Current.TrashFolder) + Measure(Path.Combine(_settings.Current.TempFolder, "downloads"));

    /// <summary>Deletes every archive retained in Trash.</summary>
    public SpaceCleanupResult ClearTrash()
        => DeleteFilesIn(_settings.Current.TrashFolder, "Trash");

    /// <summary>Deletes downloads still sitting in the temp folder.</summary>
    public SpaceCleanupResult ClearTempDownloads()
        => DeleteFilesIn(Path.Combine(_settings.Current.TempFolder, "downloads"), "temp downloads");

    /// <summary>Clears both Trash and leftover temp downloads.</summary>
    public SpaceCleanupResult CleanEverything()
    {
        var trash = ClearTrash();
        var temp = ClearTempDownloads();
        return new SpaceCleanupResult(trash.FilesDeleted + temp.FilesDeleted, trash.BytesFreed + temp.BytesFreed);
    }

    private SpaceCleanupResult DeleteFilesIn(string? folder, string label)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return new SpaceCleanupResult(0, 0);
        }

        var deleted = 0;
        long freed = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                var size = new FileInfo(file).Length;
                File.Delete(file);
                deleted++;
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete {File}.", file);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("Cleared {Count} file(s) from {Label}, freeing {Bytes} bytes.", deleted, label, freed);
        }

        return new SpaceCleanupResult(deleted, freed);
    }

    private static long Measure(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
