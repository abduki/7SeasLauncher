namespace SevenSeas.Core.Models;

/// <summary>
/// One download and its processing lifecycle.
/// A Job is the unit of truth for the pipeline; its <see cref="State"/> is persisted in SQLite.
/// </summary>
public sealed class Job
{
    /// <summary>Stable identifier; also the base name of the temp download file.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Best-known game title. Seeded from the download filename, refined by metadata.</summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>The page the download originated from, when WebView2 supplies it.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Path to the raw archive inside Temp\downloads.</summary>
    public string? DownloadedPath { get; set; }

    /// <summary>Path to the extracted game folder inside Games\{Title}.</summary>
    public string? FinalPath { get; set; }

    /// <summary>Absolute path to the discovered launch executable.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Persisted pipeline state (source of truth).</summary>
    public JobState State { get; set; } = JobState.Downloading;

    /// <summary>Human-readable reason for the current failure/warning, if any.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>True once the job has reached a terminal state.</summary>
    public bool IsTerminal => State is JobState.Done or JobState.Failed;

    /// <summary>True for states that the pipeline worker is responsible for advancing.</summary>
    public bool IsPipelineState => State is not (JobState.Downloading or JobState.Done or JobState.Failed);

    public Job Clone() => (Job)MemberwiseClone();
}
