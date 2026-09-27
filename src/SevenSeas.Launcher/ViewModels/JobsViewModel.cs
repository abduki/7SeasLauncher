using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>
/// The Jobs view: everything in flight, and what recently finished.
/// </summary>
public partial class JobsViewModel : ObservableObject
{
    private const int RecentLimit = 25;

    private readonly IJobRepository _jobs;
    private readonly PipelineWorker _pipeline;
    private readonly DownloadInterceptor _interceptor;
    private readonly DownloadIntakeService _intake;
    private readonly StatusService _status;

    [ObservableProperty] private bool hasActive;
    [ObservableProperty] private bool hasRecent;

    public JobsViewModel(
        IJobRepository jobs,
        PipelineWorker pipeline,
        DownloadInterceptor interceptor,
        DownloadIntakeService intake,
        StatusService status)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _status = status ?? throw new ArgumentNullException(nameof(status));

        _pipeline.JobChanged += _ => Refresh();
        _intake.JobChanged += _ => Refresh();
        _interceptor.ProgressChanged += OnDownloadProgress;

        Refresh();
    }

    /// <summary>True when there is nothing at all to show.</summary>
    public bool IsEmpty => !HasActive && !HasRecent;

    partial void OnHasActiveChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnHasRecentChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>Jobs still being downloaded or processed.</summary>
    public ObservableCollection<JobItemViewModel> Active { get; } = new();

    /// <summary>Recently finished jobs, newest first.</summary>
    public ObservableCollection<JobItemViewModel> Recent { get; } = new();

    public void Refresh() => OnUiThread(() =>
    {
        var all = _jobs.GetAll();

        var active = all.Where(job => !job.IsTerminal).OrderBy(job => job.CreatedAt).ToList();
        var recent = all.Where(job => job.IsTerminal)
            .OrderByDescending(job => job.UpdatedAt)
            .Take(RecentLimit)
            .ToList();

        Merge(Active, active);
        Merge(Recent, recent);

        HasActive = Active.Count > 0;
        HasRecent = Recent.Count > 0;
    });

    /// <summary>Removes finished jobs from the history.</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        var removed = _jobs.ClearFinished();
        Refresh();
        _status.Report(removed == 0 ? "Nothing finished to clear." : $"Cleared {removed} finished job(s).");
    }

    /// <summary>Stops a download that is still running, for when you change your mind.</summary>
    [RelayCommand]
    private void StopJob(JobItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_interceptor.TryCancel(item.Id))
        {
            _status.Report($"Stopping '{item.Name}'…");
        }
        else
        {
            _status.Report("Only a download still running can be stopped.");
        }
    }

    [RelayCommand]
    private void RefreshNow()
    {
        Refresh();
        _status.Report("Job list refreshed.");
    }

    private void OnDownloadProgress(DownloadProgress progress) => OnUiThread(() =>
    {
        var item = Active.FirstOrDefault(job => job.Id == progress.JobId);
        if (item is null)
        {
            return;
        }

        if (progress.Total > 0)
        {
            item.IsIndeterminate = false;
            item.Progress = progress.Percent;
        }
        else
        {
            item.IsIndeterminate = true;
        }
    });

    /// <summary>
    /// Updates rows in place rather than rebuilding, so a download's live progress is not thrown away
    /// on every state change.
    /// </summary>
    private static void Merge(ObservableCollection<JobItemViewModel> target, IReadOnlyList<Job> jobs)
    {
        var wanted = new HashSet<string>(jobs.Select(job => job.Id), StringComparer.Ordinal);

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i].Id))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var existing = target.FirstOrDefault(item => item.Id == job.Id);

            if (existing is null)
            {
                target.Insert(i, new JobItemViewModel(job));
            }
            else
            {
                existing.Apply(job);
                var current = target.IndexOf(existing);
                if (current != i)
                {
                    target.Move(current, i);
                }
            }
        }
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
