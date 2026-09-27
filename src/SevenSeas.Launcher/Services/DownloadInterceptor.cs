using System.Collections.Concurrent;
using SevenSeas.Core.Services;
using SevenSeas.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace SevenSeas.Launcher.Services;

/// <summary>Progress snapshot handed to the Shell status bar.</summary>
public sealed record DownloadProgress(string JobId, string GameName, long Received, long Total, string State)
{
    public double Percent => Total > 0 ? Math.Clamp(Received * 100.0 / Total, 0, 100) : 0;
}

/// <summary>
/// The Download Interceptor.
/// Cancels WebView2's Save-As dialog, redirects the file into Temp\downloads\{jobId}.{ext},
/// creates a Job row, reports progress and queues the job when the download completes.
/// </summary>
public sealed class DownloadInterceptor
{
    private readonly DownloadIntakeService _intake;
    private readonly ISettingsService _settings;
    private readonly PipelineWorker _pipeline;
    private readonly ILogger<DownloadInterceptor> _logger;
    private readonly ConcurrentDictionary<CoreWebView2DownloadOperation, string> _jobs = new();

    public DownloadInterceptor(
        DownloadIntakeService intake,
        ISettingsService settings,
        PipelineWorker pipeline,
        ILogger<DownloadInterceptor>? logger = null)
    {
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadInterceptor>.Instance;
    }

    public event Action<DownloadProgress>? ProgressChanged;

    public event Action<DownloadProgress>? DownloadCompleted;

    public event Action<DownloadProgress, string>? DownloadFailed;

    public void Attach(CoreWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        webView.DownloadStarting += OnDownloadStarting;
        _logger.LogInformation("Download interceptor attached to WebView2.");
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;

        try
        {
            var suggestedName = Path.GetFileName(e.ResultFilePath);
            var gameName = TitleCleaner.Clean(suggestedName);
            if (string.IsNullOrWhiteSpace(gameName))
            {
                gameName = "Unknown Game";
            }

            var extension = Path.GetExtension(suggestedName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".bin";
            }

            var downloadFolder = Path.Combine(_settings.Current.TempFolder, "downloads");
            Directory.CreateDirectory(downloadFolder);

            var job = _intake.BeginDownload(gameName, operation.Uri);
            var targetPath = Path.Combine(downloadFolder, job.Id + extension);
            _intake.AttachDownloadPath(job.Id, targetPath);

            // Redirect the download and suppress the default Save-As dialog.
            e.ResultFilePath = targetPath;
            e.Handled = true;

            _jobs[operation] = job.Id;
            operation.StateChanged += OnStateChanged;
            operation.BytesReceivedChanged += OnBytesReceivedChanged;

            _logger.LogInformation("Intercepted download {Name} as job {JobId} -> {Path}.", suggestedName, job.Id, targetPath);
            ProgressChanged?.Invoke(new DownloadProgress(
                job.Id, job.GameName, 0, ToLong(operation.TotalBytesToReceive), "Downloading"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to intercept download; letting WebView2 handle it.");
            e.Handled = false;
        }
    }

    private void OnBytesReceivedChanged(object? sender, object args)
    {
        if (sender is not CoreWebView2DownloadOperation operation ||
            !_jobs.TryGetValue(operation, out var jobId))
        {
            return;
        }

        var job = _intake.Get(jobId);
        ProgressChanged?.Invoke(new DownloadProgress(
            jobId,
            job?.GameName ?? "Download",
            operation.BytesReceived,
            ToLong(operation.TotalBytesToReceive),
            operation.State.ToString()));
    }

    private void OnStateChanged(object? sender, object args)
    {
        if (sender is not CoreWebView2DownloadOperation operation ||
            !_jobs.TryGetValue(operation, out var jobId))
        {
            return;
        }

        var job = _intake.Get(jobId);
        var progress = new DownloadProgress(
            jobId,
            job?.GameName ?? "Download",
            operation.BytesReceived,
            ToLong(operation.TotalBytesToReceive),
            operation.State.ToString());

        switch (operation.State)
        {
            case CoreWebView2DownloadState.Completed:
                Detach(operation);
                _intake.MarkDownloadCompleted(jobId, operation.ResultFilePath);
                _pipeline.NotifyJobAvailable();
                _logger.LogInformation("Download completed for job {JobId}.", jobId);
                DownloadCompleted?.Invoke(progress);
                break;

            case CoreWebView2DownloadState.Interrupted:
                Detach(operation);
                var reason = $"Download interrupted ({operation.InterruptReason}).";
                _intake.MarkDownloadFailed(jobId, reason);
                _logger.LogWarning("Download interrupted for job {JobId}: {Reason}", jobId, operation.InterruptReason);
                DownloadFailed?.Invoke(progress, reason);
                break;

            default:
                ProgressChanged?.Invoke(progress);
                break;
        }
    }

    /// <summary>Cancels an in-flight download for a job. Returns false when it is not one of ours.</summary>
    public bool TryCancel(string jobId)
    {
        foreach (var entry in _jobs)
        {
            if (!string.Equals(entry.Value, jobId, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                entry.Key.Cancel();
                _logger.LogInformation("Cancelled the download for job {JobId} at the user's request.", jobId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not cancel the download for job {JobId}.", jobId);
                return false;
            }
        }

        return false;
    }

    private void Detach(CoreWebView2DownloadOperation operation)
    {
        _jobs.TryRemove(operation, out _);
        operation.StateChanged -= OnStateChanged;
        operation.BytesReceivedChanged -= OnBytesReceivedChanged;
    }

    private static long ToLong(ulong? value) => (long)(value ?? 0);
}
