namespace SevenSeas.Core.Models;

/// <summary>What kind of trouble a download or its processing ran into.</summary>
public enum DownloadProblemKind
{
    Unknown = 0,

    /// <summary>
    /// The file vanished, was quarantined, or could not be read — almost always the antivirus
    /// reaching into the temp download folder before the pipeline can finish with it.
    /// </summary>
    PossibleAntivirusInterference,

    DiskSpace,

    Permissions,

    Network,
}

/// <summary>A diagnosis of a failed job, written to be shown to the user.</summary>
public sealed record DownloadProblemAdvice(
    DownloadProblemKind Kind,
    string Title,
    string Explanation)
{
    public bool IsAntivirusRelated => Kind == DownloadProblemKind.PossibleAntivirusInterference;
}
