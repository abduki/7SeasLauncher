namespace SevenSeas.Core.Models;

/// <summary>The archive container formats the Verifier recognises.</summary>
public enum ArchiveKind
{
    Unknown = 0,
    Zip,
    Rar,
    SevenZip,
    Tar,
    GZip,
}

/// <summary>Outcome of inspecting a downloaded file's magic bytes.</summary>
public sealed record ArchiveVerificationResult(
    bool IsValid,
    ArchiveKind Kind,
    string? Reason)
{
    public static ArchiveVerificationResult Valid(ArchiveKind kind) => new(true, kind, null);

    public static ArchiveVerificationResult Invalid(string reason) =>
        new(false, ArchiveKind.Unknown, reason);
}

/// <summary>Outcome of unpacking an archive into Games\{Title}.</summary>
public sealed record ExtractionResult(
    bool Success,
    string? DestinationFolder,
    string? Error,
    bool PasswordRequired)
{
    public static ExtractionResult Ok(string destination) => new(true, destination, null, false);

    public static ExtractionResult Failed(string error) => new(false, null, error, false);

    public static ExtractionResult NeedsPassword(string? error = null) =>
        new(false, null, error ?? "The archive requires a password.", true);
}

/// <summary>One scored executable candidate produced by the Executable Finder.</summary>
public sealed record ExecutableCandidate(string Path, int Score, long SizeBytes);

/// <summary>Outcome of scanning an extracted game folder for its launcher.</summary>
public sealed record ExecutableSearchResult(
    bool Found,
    string? ExecutablePath,
    IReadOnlyList<ExecutableCandidate> Candidates,
    bool NeedsUserChoice)
{
    public static ExecutableSearchResult None(IReadOnlyList<ExecutableCandidate> candidates) =>
        new(false, null, candidates, false);

    public static ExecutableSearchResult Chosen(string path, IReadOnlyList<ExecutableCandidate> candidates) =>
        new(true, path, candidates, false);

    public static ExecutableSearchResult Ambiguous(IReadOnlyList<ExecutableCandidate> candidates) =>
        new(false, null, candidates, true);
}

/// <summary>Metadata resolved for a downloaded archive.</summary>
/// <param name="Matched">True when SteamGridDB actually recognised the game, rather than us guessing.</param>
public sealed record GameMetadata(string Title, string? CoverUrl, bool Matched = false)
{
    public static GameMetadata Fallback(string title) => new(title, null, false);
}
