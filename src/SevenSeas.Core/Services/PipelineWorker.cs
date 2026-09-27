using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// The single background worker that walks one job at a time through every processing stage.
/// Downloads may be parallel; processing is serial.
/// </summary>
public sealed class PipelineWorker : IAsyncDisposable
{
    private readonly IJobRepository _jobs;
    private readonly IGameRepository _games;
    private readonly ISettingsService _settings;
    private readonly IArchiveVerifier _verifier;
    private readonly IMetadataService _metadata;
    private readonly IArchiveExtractor _extractor;
    private readonly IExecutableFinder _executableFinder;
    private readonly IArchivePasswordSource _passwordSource;
    private readonly IUserInteraction _userInteraction;
    private readonly TrashService _trash;
    private readonly IClock _clock;
    private readonly ILogger<PipelineWorker> _logger;

    private readonly SemaphoreSlim _signal = new(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PipelineWorker(
        IJobRepository jobs,
        IGameRepository games,
        ISettingsService settings,
        IArchiveVerifier verifier,
        IMetadataService metadata,
        IArchiveExtractor extractor,
        IExecutableFinder executableFinder,
        IArchivePasswordSource passwordSource,
        IUserInteraction userInteraction,
        TrashService trash,
        IClock? clock = null,
        ILogger<PipelineWorker>? logger = null)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _executableFinder = executableFinder ?? throw new ArgumentNullException(nameof(executableFinder));
        _passwordSource = passwordSource ?? throw new ArgumentNullException(nameof(passwordSource));
        _userInteraction = userInteraction ?? throw new ArgumentNullException(nameof(userInteraction));
        _trash = trash ?? throw new ArgumentNullException(nameof(trash));
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PipelineWorker>.Instance;
    }

    /// <summary>Raised after every persisted state transition and on terminal outcomes.</summary>
    public event Action<Job>? JobChanged;

    /// <summary>True while a job is being processed by this worker.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Starts the background pump. The loop exits when the token is cancelled.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("Pipeline worker started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        if (cts is null || loop is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        finally
        {
            cts.Dispose();
            _cts = null;
            _loop = null;
            _logger.LogInformation("Pipeline worker stopped.");
        }
    }

    /// <summary>Wakes the worker because a job entered the Queued state.</summary>
    public void NotifyJobAvailable()
    {
        try
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Another thread already signalled; nothing to do.
        }
    }

    /// <summary>
    /// Requeues jobs left behind by a crash. Downloading jobs cannot truly resume,
    /// so they are either re-queued (when a file path exists) or failed.
    /// </summary>
    public async Task<int> ResumeInterruptedAsync(CancellationToken cancellationToken = default)
    {
        var interrupted = _jobs.GetInterrupted();
        if (interrupted.Count == 0)
        {
            return 0;
        }

        var resume = true;
        if (_settings.Current.ResumeInterruptedJobs)
        {
            resume = await _userInteraction
                .ConfirmResumeInterruptedJobsAsync(interrupted, cancellationToken)
                .ConfigureAwait(false);
        }

        var requeued = 0;
        foreach (var job in interrupted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!resume)
            {
                Fail(job, "Resume was declined; the job was not completed.");
                continue;
            }

            if (job.State == JobState.Downloading && string.IsNullOrWhiteSpace(job.DownloadedPath))
            {
                Fail(job, "The app closed while this download was still in progress.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(job.DownloadedPath) || !File.Exists(job.DownloadedPath))
            {
                Fail(job, "The downloaded file for this job is missing.");
                continue;
            }

            Transition(job, JobState.Queued);
            requeued++;
        }

        if (requeued > 0)
        {
            _logger.LogInformation("Requeued {Count} interrupted job(s).", requeued);
            NotifyJobAvailable();
        }

        return requeued;
    }

    /// <summary>Processes the next Queued job, if any. Returns false when the queue is empty.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var job = _jobs.GetNextQueued();
        if (job is null)
        {
            return false;
        }

        await ProcessJobAsync(job, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Drains the queue. Used by tests and by startup catch-up processing.</summary>
    public async Task<int> ProcessAllQueuedAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0;
        while (await ProcessNextAsync(cancellationToken).ConfigureAwait(false))
        {
            processed++;
        }

        return processed;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The pipeline loop hit an unexpected error; continuing.");
                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessJobAsync(Job job, CancellationToken cancellationToken)
    {
        IsBusy = true;
        _logger.LogInformation("Pipeline picked up job {JobId} ('{GameName}').", job.Id, job.GameName);

        try
        {
            // Step 1 — Archive Verifier.
            Transition(job, JobState.Verifying);
            var verification = _verifier.Verify(job.DownloadedPath ?? string.Empty);
            if (!verification.IsValid)
            {
                Fail(job, verification.Reason ?? "The downloaded file is not a valid archive.");
                return;
            }

            // Step 2 — Metadata Service.
            Transition(job, JobState.FetchingMetadata);
            var metadata = await _metadata
                .LookupAsync(job.GameName, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(metadata.Title))
            {
                job.GameName = metadata.Title;
            }

            // Step 3 — Archive Extractor.
            Transition(job, JobState.Extracting);
            var destination = Path.Combine(
                _settings.Current.GamesFolder,
                PathSafety.SanitizeFolderName(job.GameName));
            var extraction = await ExtractWithPasswordHandlingAsync(job, destination, cancellationToken)
                .ConfigureAwait(false);
            if (!extraction.Success)
            {
                Fail(job, extraction.Error ?? "Extraction failed.");
                return;
            }

            job.FinalPath = extraction.DestinationFolder;

            // Archives often unpack to a wrapper folder, or ship the game plus a separate
            // fix/repair folder that has to be copied over it. Tidy both up before hunting for the exe.
            try
            {
                var normalized = ExtractionNormalizer.Normalize(job.FinalPath!, _logger);
                if (normalized.Flattened || normalized.FixFoldersMerged > 0)
                {
                    _logger.LogInformation(
                        "Normalised the extracted folder for job {JobId}: flattened={Flattened}, fix folders merged={Merged}.",
                        job.Id, normalized.Flattened, normalized.FixFoldersMerged);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not normalise the extracted folder for job {JobId}; continuing.", job.Id);
            }

            // Step 4 — Executable Finder.
            Transition(job, JobState.FindingExecutable);
            var search = _executableFinder.Find(job.FinalPath!);
            if (!search.Found && search.NeedsUserChoice)
            {
                var chosen = await _userInteraction
                    .RequestExecutableChoiceAsync(search.Candidates, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(chosen) && File.Exists(chosen))
                {
                    search = ExecutableSearchResult.Chosen(chosen, search.Candidates);
                }
            }

            if (!search.Found || string.IsNullOrWhiteSpace(search.ExecutablePath))
            {
                Fail(job, "No executable found. Use the manual picker to choose the game's launcher.");
                return;
            }

            job.ExecutablePath = search.ExecutablePath;

            // If SteamGridDB did not recognise the game, the executable's own name beats the archive
            // filename, which carries release-group noise like "v1.5-CODEX".
            if (!metadata.Matched)
            {
                var exeTitle = TitleCleaner.Clean(Path.GetFileNameWithoutExtension(search.ExecutablePath));
                if (!string.IsNullOrWhiteSpace(exeTitle))
                {
                    job.GameName = exeTitle;
                }
            }

            // Library Repo writes the final record.
            var game = new Game
            {
                Id = job.Id,
                Title = job.GameName,
                InstallPath = job.FinalPath!,
                ExecutablePath = job.ExecutablePath!,
                CoverUrl = metadata.CoverUrl,
                AddedAt = _clock.UtcNow,
            };
            _games.Add(game);

            Transition(job, JobState.Done);
            _trash.MoveToTrash(job.DownloadedPath, job.Id);
            _logger.LogInformation("Job {JobId} finished: '{Title}' installed at {Path}.", job.Id, game.Title, game.InstallPath);
        }
        catch (OperationCanceledException)
        {
            // Leave the job in its current non-terminal state so it resumes on next launch.
            _logger.LogWarning("Job {JobId} was interrupted by shutdown.", job.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed with an unexpected error.", job.Id);
            Fail(job, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ExtractionResult> ExtractWithPasswordHandlingAsync(
        Job job,
        string destination,
        CancellationToken cancellationToken)
    {
        var hint = _passwordSource.GetPasswordHint(job.SourceUrl);
        var password = string.IsNullOrWhiteSpace(hint) ? null : hint;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await _extractor
                .ExtractAsync(job.DownloadedPath ?? string.Empty, destination, password, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success || !result.PasswordRequired)
            {
                return result;
            }

            _logger.LogInformation("Archive for job {JobId} needs a password (attempt {Attempt}).", job.Id, attempt + 1);

            var entered = await _userInteraction
                .RequestArchivePasswordAsync(job.DownloadedPath ?? string.Empty, hint, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(entered))
            {
                return ExtractionResult.Failed("The archive requires a password that was not supplied.");
            }

            password = entered;
        }

        return ExtractionResult.Failed("The archive password was not accepted after 3 attempts.");
    }

    private void Transition(Job job, JobState state)
    {
        job.State = state;
        job.UpdatedAt = _clock.UtcNow;
        _jobs.Update(job);
        _logger.LogInformation("Job {JobId} -> {State}.", job.Id, state);
        JobChanged?.Invoke(job.Clone());
    }

    private void Fail(Job job, string reason)
    {
        job.State = JobState.Failed;
        job.LastError = reason;
        job.UpdatedAt = _clock.UtcNow;
        _jobs.Update(job);
        _trash.MoveToTrash(job.DownloadedPath, job.Id);
        _logger.LogWarning("Job {JobId} failed: {Reason}", job.Id, reason);
        JobChanged?.Invoke(job.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _signal.Dispose();
    }
}
