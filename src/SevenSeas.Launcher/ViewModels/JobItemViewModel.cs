using CommunityToolkit.Mvvm.ComponentModel;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Launcher.ViewModels;

/// <summary>One row on the Jobs view: a download and where its processing has got to.</summary>
public partial class JobItemViewModel : ObservableObject
{
    public JobItemViewModel(Job job)
        => Job = job ?? throw new ArgumentNullException(nameof(job));

    public Job Job { get; }

    public string Id => Job.Id;

    public string Name => string.IsNullOrWhiteSpace(Job.GameName) ? "Untitled download" : Job.GameName;

    public bool IsActive => !Job.IsTerminal;

    public string StateText => Describe(Job.State);

    public string UpdatedText => Job.UpdatedAt.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// Where this download actually came from. A fake advert button downloads from somewhere you did
    /// not choose, so showing the origin is the cheapest way to make that visible.
    /// </summary>
    public string SourceText
    {
        get
        {
            var host = SiteNaming.NormalizedHost(Job.SourceUrl);
            return host is null ? string.Empty : $"from {host}";
        }
    }

    public string Detail => !string.IsNullOrWhiteSpace(Job.LastError)
        ? Job.LastError!
        : Job.State == JobState.Done
            ? Job.FinalPath ?? string.Empty
            : Job.State == JobState.Downloading
                ? "Saving to the temp folder"
                : string.Empty;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isIndeterminate = true;

    /// <summary>Friendly wording for the pipeline states.</summary>
    public static string Describe(JobState state) => state switch
    {
        JobState.Downloading => "Downloading",
        JobState.Queued => "Waiting to be processed",
        JobState.Verifying => "Checking the archive",
        JobState.FetchingMetadata => "Looking up cover art",
        JobState.Extracting => "Unpacking",
        JobState.FindingExecutable => "Finding the game's executable",
        JobState.Done => "Added to your library",
        JobState.Failed => "Failed",
        _ => state.ToString(),
    };

    public void Apply(Job job)
    {
        Job.GameName = job.GameName;
        Job.State = job.State;
        Job.LastError = job.LastError;
        Job.FinalPath = job.FinalPath;
        Job.UpdatedAt = job.UpdatedAt;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(UpdatedText));

        // Only a download has a measurable percentage; the rest are indeterminate.
        if (job.State != JobState.Downloading)
        {
            IsIndeterminate = true;
            Progress = 0;
        }
    }
}
