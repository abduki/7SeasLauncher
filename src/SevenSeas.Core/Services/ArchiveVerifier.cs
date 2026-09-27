using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// Confirms a downloaded file is a real archive by inspecting magic bytes.
/// </summary>
public sealed class ArchiveVerifier : IArchiveVerifier
{
    // Signatures are the first bytes of the container.
    private static readonly byte[] ZipLocal = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] ZipEmpty = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] ZipSpanned = { 0x50, 0x4B, 0x07, 0x08 };
    private static readonly byte[] Rar4 = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
    private static readonly byte[] Rar5 = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
    private static readonly byte[] SevenZip = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
    private static readonly byte[] GZip = { 0x1F, 0x8B };

    private const int HeaderSize = 512;
    private const int TarSignatureOffset = 257;

    private readonly ILogger<ArchiveVerifier> _logger;

    public ArchiveVerifier(ILogger<ArchiveVerifier>? logger = null)
        => _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchiveVerifier>.Instance;

    public ArchiveVerificationResult Verify(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ArchiveVerificationResult.Invalid("No downloaded file was recorded for this job.");
        }

        if (!File.Exists(filePath))
        {
            return ArchiveVerificationResult.Invalid($"The downloaded file no longer exists: {filePath}");
        }

        long length;
        try
        {
            length = new FileInfo(filePath).Length;
        }
        catch (IOException ex)
        {
            return ArchiveVerificationResult.Invalid($"The downloaded file could not be read: {ex.Message}");
        }

        if (length == 0)
        {
            return ArchiveVerificationResult.Invalid("The downloaded file is empty (0 bytes).");
        }

        var header = new byte[HeaderSize];
        int read;
        try
        {
            using var stream = File.OpenRead(filePath);
            read = ReadAtLeast(stream, header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ArchiveVerificationResult.Invalid($"The downloaded file could not be opened: {ex.Message}");
        }

        if (read < 4)
        {
            return ArchiveVerificationResult.Invalid("The downloaded file is too small to be an archive.");
        }

        var span = header.AsSpan(0, read);

        ArchiveKind? kind = null;
        if (StartsWith(span, ZipLocal) || StartsWith(span, ZipEmpty) || StartsWith(span, ZipSpanned))
        {
            kind = ArchiveKind.Zip;
        }
        else if (StartsWith(span, Rar5) || StartsWith(span, Rar4))
        {
            kind = ArchiveKind.Rar;
        }
        else if (StartsWith(span, SevenZip))
        {
            kind = ArchiveKind.SevenZip;
        }
        else if (StartsWith(span, GZip))
        {
            kind = ArchiveKind.GZip;
        }
        else if (read >= TarSignatureOffset + 5 &&
                 span.Slice(TarSignatureOffset, 5).SequenceEqual("ustar"u8))
        {
            kind = ArchiveKind.Tar;
        }

        if (kind is null)
        {
            _logger.LogWarning("File {File} is not a recognised archive.", filePath);
            return ArchiveVerificationResult.Invalid(
                "Not a valid archive: the file does not start with a known ZIP, RAR, 7z, TAR or GZip signature.");
        }

        _logger.LogInformation("Verified {File} as {Kind}.", filePath, kind);
        return ArchiveVerificationResult.Valid(kind.Value);
    }

    private static int ReadAtLeast(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        => data.Length >= signature.Length && data[..signature.Length].SequenceEqual(signature);
}
