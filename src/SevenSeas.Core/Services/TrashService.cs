using SevenSeas.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// Owns the Trash\ folder: failed and consumed archives are retained there.
/// "Never lose the user's data" — the archive is moved, never deleted outright.
/// </summary>
public sealed class TrashService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<TrashService> _logger;

    public TrashService(ISettingsService settings, ILogger<TrashService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TrashService>.Instance;
    }

    /// <summary>Moves an archive into Trash\{jobId}{ext}. Returns the new path, or null when nothing moved.</summary>
    public string? MoveToTrash(string? filePath, string jobId)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var trashFolder = _settings.Current.TrashFolder;
        if (string.IsNullOrWhiteSpace(trashFolder))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(trashFolder);
            var extension = Path.GetExtension(filePath);
            var target = Path.Combine(trashFolder, $"{jobId}{extension}");
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(filePath, target, overwrite: true);
            _logger.LogInformation("Moved archive {Source} to Trash as {Target}.", filePath, target);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not move {File} to Trash.", filePath);
            return null;
        }
    }

    /// <summary>Deletes Trash entries older than the configured retention window. Returns the count removed.</summary>
    public int CleanupExpired()
    {
        var trashFolder = _settings.Current.TrashFolder;
        var retentionDays = Math.Max(0, _settings.Current.TrashRetentionDays);

        if (string.IsNullOrWhiteSpace(trashFolder) || !Directory.Exists(trashFolder))
        {
            return 0;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(trashFolder))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not remove expired Trash entry {File}.", file);
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Count} expired Trash entries.", removed);
        }

        return removed;
    }
}
