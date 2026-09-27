namespace SevenSeas.Core.Models;

/// <summary>
/// The lifecycle states of a single download job.
/// The pipeline walks a job through these states in a fixed order,
/// persisted to SQLite after every transition.
/// </summary>
public enum JobState
{
    /// <summary>WebView2 is actively writing the file into Temp\downloads.</summary>
    Downloading = 0,

    /// <summary>Download finished; waiting for the serial pipeline worker.</summary>
    Queued = 1,

    /// <summary>Archive Verifier is checking magic bytes / size.</summary>
    Verifying = 2,

    /// <summary>Metadata Service is querying SteamGridDB.</summary>
    FetchingMetadata = 3,

    /// <summary>Archive Extractor is unpacking into Games\{Title}.</summary>
    Extracting = 4,

    /// <summary>Executable Finder is scoring .exe candidates.</summary>
    FindingExecutable = 5,

    /// <summary>Library Repo has written the Games row. Terminal.</summary>
    Done = 6,

    /// <summary>Pipeline failed. Archive is retained in Trash. Terminal.</summary>
    Failed = 7,
}
