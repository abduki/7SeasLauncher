using CommunityToolkit.Mvvm.ComponentModel;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>Backs the Shell status bar.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IJobRepository _jobs;
    private readonly DownloadIntakeService _intake;
    private readonly PipelineWorker _pipeline;
    private readonly DownloadInterceptor _interceptor;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private string detailText = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    private int activeJobCount;

    public MainViewModel(
        IJobRepository jobs,
        DownloadIntakeService intake,
        PipelineWorker pipeline,
        DownloadInterceptor interceptor,
        StatusService status)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));

        // Everything reports transient feedback here instead of drawing its own inline status text.
        status.Reported += message =>
        {
            StatusText = message;
            DetailText = string.Empty;
        };

        _interceptor.ProgressChanged += OnProgress;
        _interceptor.DownloadCompleted += OnCompleted;
        _interceptor.DownloadFailed += OnFailed;
        _pipeline.JobChanged += OnJobChanged;
        _intake.JobChanged += OnJobChanged;

        Refresh();
    }

    private void OnProgress(DownloadProgress progress)
    {
        IsProgressVisible = true;
        ProgressValue = progress.Percent;
        StatusText = $"Downloading {progress.GameName}…";
        DetailText = progress.Total > 0
            ? $"{Format(progress.Received)} of {Format(progress.Total)}"
            : Format(progress.Received);
    }

    private void OnCompleted(DownloadProgress progress)
    {
        IsProgressVisible = false;
        StatusText = $"{progress.GameName}: download complete, queued for processing.";
        DetailText = string.Empty;
        Refresh();
    }

    private void OnFailed(DownloadProgress progress, string reason)
    {
        IsProgressVisible = false;
        StatusText = $"{progress.GameName}: download failed.";
        DetailText = reason;
        Refresh();
    }

    private void OnJobChanged(Job job)
    {
        StatusText = job.State switch
        {
            JobState.Done => $"{job.GameName} added to your library.",
            JobState.Failed => $"{job.GameName} failed: {job.LastError}",
            JobState.Queued => $"{job.GameName} is queued for processing.",
            _ => $"{job.GameName}: {job.State}",
        };
        Refresh();
    }

    public void Refresh()
    {
        var active = _jobs.GetAll().Where(j => !j.IsTerminal).ToList();
        ActiveJobCount = active.Count;

        if (active.Count == 0)
        {
            if (string.IsNullOrEmpty(DetailText))
            {
                IsProgressVisible = false;
            }
        }
    }

    private static string Format(long bytes)
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
