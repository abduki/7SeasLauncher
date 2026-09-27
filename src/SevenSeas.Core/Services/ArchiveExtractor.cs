using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace SevenSeas.Core.Services;

/// <summary>
/// Unpacks an archive into Games\{Title} with SharpCompress.
/// Rejects path-traversal entries, prompts for passwords, and is safe to re-run.
/// </summary>
public sealed class ArchiveExtractor : IArchiveExtractor
{
    private readonly ILogger<ArchiveExtractor> _logger;

    public ArchiveExtractor(ILogger<ArchiveExtractor>? logger = null)
        => _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchiveExtractor>.Instance;

    public Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Extract(archivePath, destinationFolder, password, cancellationToken), cancellationToken);

    private ExtractionResult Extract(string archivePath, string destinationFolder, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return ExtractionResult.Failed($"Archive not found: {archivePath}");
        }

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return ExtractionResult.Failed("No destination folder was supplied.");
        }

        var destinationRoot = Path.GetFullPath(destinationFolder);

        IArchive archive;
        try
        {
            var options = new ReaderOptions();
            if (!string.IsNullOrEmpty(password))
            {
                options.Password = password;
            }

            archive = ArchiveFactory.OpenArchive(archivePath, options);
        }
        catch (Exception ex) when (IsPasswordRelated(ex))
        {
            _logger.LogWarning("Archive {Archive} is password protected.", archivePath);
            return ExtractionResult.NeedsPassword("The archive is password protected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open archive {Archive}.", archivePath);
            return ExtractionResult.Failed($"Could not open the archive: {ex.Message}");
        }

        try
        {
            using (archive)
            {
                Directory.CreateDirectory(destinationRoot);
                var extracted = 0;

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        continue;
                    }

                    // Symlink entries are skipped: we never create links out of the game folder.
                    if (!string.IsNullOrEmpty(entry.LinkTarget))
                    {
                        _logger.LogWarning("Skipped symbolic-link entry {Key} in {Archive}.", entry.Key, archivePath);
                        continue;
                    }

                    var key = entry.Key;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (PathSafety.LooksLikeTraversal(key))
                    {
                        _logger.LogWarning("Rejected traversal entry {Key} in {Archive}.", key, archivePath);
                        return ExtractionResult.Failed(
                            $"The archive contains an unsafe path and was rejected: {key}");
                    }

                    var target = Path.Combine(destinationRoot, key.Replace('/', Path.DirectorySeparatorChar));
                    if (!PathSafety.IsWithin(destinationRoot, target, out var fullTarget))
                    {
                        _logger.LogWarning("Rejected out-of-root entry {Key} in {Archive}.", key, archivePath);
                        return ExtractionResult.Failed(
                            $"The archive contains an unsafe path and was rejected: {key}");
                    }

                    try
                    {
                        var parent = Path.GetDirectoryName(fullTarget);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        using var entryStream = entry.OpenEntryStream();
                        using var fileStream = new FileStream(fullTarget, FileMode.Create, FileAccess.Write, FileShare.None);
                        entryStream.CopyTo(fileStream);
                        extracted++;
                    }
                    catch (Exception ex) when (IsPasswordRelated(ex))
                    {
                        _logger.LogWarning("Password required/incorrect for {Key} in {Archive}.", key, archivePath);
                        return ExtractionResult.NeedsPassword("The archive requires a password.");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogError(ex, "Failed writing {File}.", fullTarget);
                        return ExtractionResult.Failed($"Failed to extract '{key}': {ex.Message}");
                    }
                }

                _logger.LogInformation("Extracted {Count} files from {Archive} into {Destination}.",
                    extracted, archivePath, destinationRoot);
                return ExtractionResult.Ok(destinationRoot);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsPasswordRelated(ex))
        {
            return ExtractionResult.NeedsPassword("The archive requires a password.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction of {Archive} failed.", archivePath);
            return ExtractionResult.Failed($"Extraction failed: {ex.Message}");
        }
    }

    private static bool IsPasswordRelated(Exception ex)
    {
        var typeName = ex.GetType().Name;
        if (typeName.Contains("Cryptographic", StringComparison.OrdinalIgnoreCase) ||
            typeName.Contains("Password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = ex.Message;
        return message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("crc", StringComparison.OrdinalIgnoreCase) && message.Contains("key", StringComparison.OrdinalIgnoreCase);
    }
}
