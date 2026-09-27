using SevenSeas.Core.Models;

namespace SevenSeas.Core.Services;

/// <summary>
/// Turns a raw failure message into something the user can act on.
/// <para>
/// The case that matters is antivirus interference. 7SeasLauncher downloads into a temp folder and only then
/// unpacks, so Defender scans the archive as it is written and can quarantine it mid-flight. Those
/// symptoms are recognisable and worth explaining rather than surfacing as "file no longer exists".
/// </para>
/// </summary>
public static class DownloadProblemAdvisor
{
    private static readonly string[] AntivirusSignals =
    {
        "no longer exists",
        "empty (0 bytes)",
        "access is denied",
        "access to the path",
        "being used by another process",
        "cannot access the file",
        "virus",
        "quarantine",
        "threat",
        "malware",
        "malicious",
        "is missing",
        "0x800700e1",
        "operation did not complete successfully",
        "blocked by",
    };

    private static readonly string[] AntivirusInterruptSignals =
    {
        "fileaccessdenied",
        "filefailed",
        "fileblocked",
        "filemalicious",
        "securitycheckfailed",
    };

    /// <summary>Classifies a failure, or returns null when there is nothing useful to say.</summary>
    public static DownloadProblemAdvice? Advise(string? error, string? interruptReason)
    {
        var errorText = error ?? string.Empty;
        var reasonText = interruptReason ?? string.Empty;
        var combined = (errorText + " " + reasonText).Trim();

        if (combined.Length == 0)
        {
            return null;
        }

        var lower = combined.ToLowerInvariant();
        var reasonCompact = reasonText.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);

        var looksAntivirus = false;
        for (var i = 0; i < AntivirusSignals.Length; i++)
        {
            if (lower.Contains(AntivirusSignals[i], StringComparison.Ordinal))
            {
                looksAntivirus = true;
                break;
            }
        }

        if (!looksAntivirus)
        {
            for (var i = 0; i < AntivirusInterruptSignals.Length; i++)
            {
                if (reasonCompact.Contains(AntivirusInterruptSignals[i], StringComparison.Ordinal))
                {
                    looksAntivirus = true;
                    break;
                }
            }
        }

        if (looksAntivirus)
        {
            return new DownloadProblemAdvice(
                DownloadProblemKind.PossibleAntivirusInterference,
                "Your antivirus may have blocked this download",
                "Windows Security, or another antivirus, can quarantine a game archive while 7SeasLauncher is " +
                "still writing or unpacking it. The download appears to finish, then the file is suddenly " +
                "missing or unreadable. Excluding 7SeasLauncher's own folders stops this from happening.");
        }

        if (lower.Contains("space", StringComparison.Ordinal) &&
            (lower.Contains("disk", StringComparison.Ordinal) || lower.Contains("drive", StringComparison.Ordinal)))
        {
            return new DownloadProblemAdvice(
                DownloadProblemKind.DiskSpace,
                "Not enough free disk space",
                "The drive ran out of room while downloading or unpacking. Free some space and try again.");
        }

        if (lower.Contains("network", StringComparison.Ordinal) ||
            lower.Contains("connection", StringComparison.Ordinal) ||
            lower.Contains("timed out", StringComparison.Ordinal) ||
            lower.Contains("name resolution", StringComparison.Ordinal))
        {
            return new DownloadProblemAdvice(
                DownloadProblemKind.Network,
                "The download was interrupted",
                "The connection dropped or the server stopped responding. Trying again usually works.");
        }

        return null;
    }
}
