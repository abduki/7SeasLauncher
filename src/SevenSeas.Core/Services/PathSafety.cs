namespace SevenSeas.Core.Services;

/// <summary>
/// Defensive helpers shared by extraction and folder naming.
/// Path traversal protection is a security control, not a nicety.
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Returns true when <paramref name="candidate"/> resolves inside <paramref name="root"/>.
    /// Uses canonical full paths so '..' segments and rooted entries cannot escape.
    /// </summary>
    public static bool IsWithin(string root, string candidate, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when an archive entry key tries to escape via a rooted or '..' path.</summary>
    public static bool LooksLikeTraversal(string entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            return false;
        }

        var key = entryKey.Replace('\\', '/');
        if (key.StartsWith('/') || Path.IsPathRooted(entryKey))
        {
            return true;
        }

        // A Windows drive-relative entry such as "C:evil.exe".
        if (key.Length >= 2 && char.IsLetter(key[0]) && key[1] == ':')
        {
            return true;
        }

        return key.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
    }

    /// <summary>Turns an arbitrary game title into a safe single folder name.</summary>
    public static string SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Unknown Game";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        if (result.Length == 0)
        {
            result = "Unknown Game";
        }

        if (result.Length > 120)
        {
            result = result[..120].Trim().TrimEnd('.');
        }

        return result;
    }
}
