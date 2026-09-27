using Microsoft.Extensions.Logging;

namespace SevenSeas.Launcher.Services;

/// <summary>
/// The Connection-4 fallback: when DownloadStarting never fires (JS blobs, window.open),
/// watch the user's Downloads folder and offer to process anything that lands there.
/// <para>
/// The tricky part is that WebView2 stages <em>managed</em> downloads here too, as bare
/// <c>&lt;guid&gt;.tmp</c> files, before the interceptor moves them into Temp\downloads.
/// Those must never be offered to the user, and neither should a file that is still being written.
/// </para>
/// </summary>
public sealed class DownloadsFolderWatcher : IDisposable
{
    private static readonly string[] IgnoredExtensions =
    {
        ".tmp", ".crdownload", ".partial", ".download", ".opdownload", ".part",
    };

    private const int SettleAttempts = 6;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(700);

    private readonly ILogger<DownloadsFolderWatcher> _logger;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;

    public DownloadsFolderWatcher(ILogger<DownloadsFolderWatcher>? logger = null)
        => _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadsFolderWatcher>.Instance;

    public event Action<string>? FileDetected;

    public string DownloadsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public void Start()
    {
        lock (_gate)
        {
            if (_watcher is not null)
            {
                return;
            }

            if (!Directory.Exists(DownloadsFolder))
            {
                _logger.LogInformation("Downloads folder {Folder} does not exist; watcher disabled.", DownloadsFolder);
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(DownloadsFolder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true,
                };
                _watcher.Created += OnCreated;
                _logger.LogInformation("Watching {Folder} for downloads WebView2 did not report.", DownloadsFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not watch the Downloads folder.");
                _watcher = null;
            }
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
        => _ = ConfirmAndNotifyAsync(e.FullPath);

    /// <summary>
    /// Ignores scratch files outright, then waits for the file to stop growing so a half-written
    /// download is never offered for processing.
    /// </summary>
    private async Task ConfirmAndNotifyAsync(string path)
    {
        var extension = Path.GetExtension(path);
        if (IgnoredExtensions.Any(ignored => string.Equals(ignored, extension, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Ignoring WebView2 scratch file {File}.", path);
            return;
        }

        long previousSize = -1;

        for (var attempt = 0; attempt < SettleAttempts; attempt++)
        {
            await Task.Delay(SettleDelay).ConfigureAwait(false);

            if (!File.Exists(path))
            {
                // Gone: WebView2 staged it here and moved it itself.
                _logger.LogDebug("Ignoring vanished download {File}.", path);
                return;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                return;
            }

            if (size > 0 && size == previousSize)
            {
                break;
            }

            previousSize = size;
        }

        if (!File.Exists(path))
        {
            return;
        }

        _logger.LogInformation("Unmanaged download detected: {File}", path);
        FileDetected?.Invoke(path);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnCreated;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
