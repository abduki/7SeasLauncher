using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// The Core half of the download interceptor.
/// The WebView2 adapter in the Launcher calls these methods; all job-state logic lives here so it is testable.
/// </summary>
public sealed class DownloadIntakeService
{
    private readonly IJobRepository _jobs;
    private readonly IClock _clock;
    private readonly ILogger<DownloadIntakeService> _logger;

    public DownloadIntakeService(
        IJobRepository jobs,
        IClock? clock = null,
        ILogger<DownloadIntakeService>? logger = null)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadIntakeService>.Instance;
    }

    /// <summary>Raised whenever a job is created or changes state, so the Shell status bar can update.</summary>
    public event Action<Job>? JobChanged;

    /// <summary>Creates a Downloading job the moment WebView2 fires DownloadStarting.</summary>
    public Job BeginDownload(string gameName, string? sourceUrl, string? downloadedPath = null)
    {
        var job = new Job
        {
            Id = Guid.NewGuid().ToString("N"),
            GameName = string.IsNullOrWhiteSpace(gameName) ? "Unknown Game" : gameName.Trim(),
            SourceUrl = sourceUrl,
            DownloadedPath = downloadedPath,
            State = JobState.Downloading,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow,
        };

        _jobs.Create(job);
        _logger.LogInformation("Download started: job {JobId} for '{GameName}'.", job.Id, job.GameName);
        JobChanged?.Invoke(job.Clone());
        return job;
    }

    /// <summary>Records where WebView2 is actually writing a download once the path is known.</summary>
    public void AttachDownloadPath(string jobId, string downloadedPath)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        job.DownloadedPath = downloadedPath;
        job.UpdatedAt = _clock.UtcNow;
        _jobs.Update(job);
        JobChanged?.Invoke(job.Clone());
    }

    /// <summary>Moves a finished download into the queue for the serial pipeline worker.</summary>
    public Job? MarkDownloadCompleted(string jobId, string? downloadedPath = null)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(downloadedPath))
        {
            job.DownloadedPath = downloadedPath;
        }

        job.State = JobState.Queued;
        job.UpdatedAt = _clock.UtcNow;
        _jobs.Update(job);
        _logger.LogInformation("Download complete: job {JobId} queued.", job.Id);
        JobChanged?.Invoke(job.Clone());
        return job;
    }

    /// <summary>Marks a download that never produced a usable file as failed.</summary>
    public Job? MarkDownloadFailed(string jobId, string error)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return null;
        }

        job.State = JobState.Failed;
        job.LastError = error;
        job.UpdatedAt = _clock.UtcNow;
        _jobs.Update(job);
        _logger.LogWarning("Download failed: job {JobId}: {Error}", job.Id, error);
        JobChanged?.Invoke(job.Clone());
        return job;
    }

    public Job? Get(string jobId) => _jobs.Get(jobId);

    public IReadOnlyList<Job> GetActiveJobs() =>
        _jobs.GetAll().Where(j => !j.IsTerminal).ToList();

    private Job? RequireJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return null;
        }

        var job = _jobs.Get(jobId);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} was not found.", jobId);
        }

        return job;
    }
}
