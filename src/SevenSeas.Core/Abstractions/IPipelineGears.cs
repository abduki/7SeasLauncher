using SevenSeas.Core.Models;

namespace SevenSeas.Core.Abstractions;

/// <summary>Checks a downloaded file is a real archive.</summary>
public interface IArchiveVerifier
{
    ArchiveVerificationResult Verify(string filePath);
}

/// <summary>Resolves a game's title and cover art from SteamGridDB.</summary>
public interface IMetadataService
{
    Task<GameMetadata> LookupAsync(string fileOrGameName, CancellationToken cancellationToken = default);
}

/// <summary>Unpacks an archive into Games\{Title}.</summary>
public interface IArchiveExtractor
{
    Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password,
        CancellationToken cancellationToken = default);
}

/// <summary>Scores .exe files and picks the launcher.</summary>
public interface IExecutableFinder
{
    ExecutableSearchResult Find(string installFolder);

    /// <summary>
    /// Every executable under a folder with its score, including ones the finder would reject as
    /// noise, so the user can override the choice from the library.
    /// </summary>
    IReadOnlyList<ExecutableCandidate> ListCandidates(string installFolder);
}

