using System.Text.RegularExpressions;

namespace SevenSeas.Core.Services;

/// <summary>
/// Turns a download filename into a plausible game title for searching and for the fallback title.
/// </summary>
public static partial class TitleCleaner
{
    private static readonly HashSet<string> JunkTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "fitgirl", "repack", "codex", "plaza", "skidrow", "razor1911", "gog", "multi",
        "multiplayer", "pc", "win64", "x64", "update", "crack", "cracked", "portable",
        "full", "version", "final", "iso", "setup", "installer", "rld", "reloaded",
        "elamigos", "dodi", "gdrive", "torrent", "dlc", "bonus", "soundtrack",
    };

    [GeneratedRegex(@"[\[\]\(\)\{\}][^\[\]\(\)\{\}]*[\[\]\(\)\{\}]", RegexOptions.Compiled)]
    private static partial Regex BracketGroup();

    [GeneratedRegex(@"^v?\d+(\.\d+)*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex VersionToken();

    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var name = raw.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = name[..dot];
        }

        name = BracketGroup().Replace(name, " ");
        name = name.Replace('.', ' ').Replace('_', ' ');

        var kept = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !JunkTokens.Contains(token) && !VersionToken().IsMatch(token))
            .ToArray();

        var result = string.Join(' ', kept).Trim();
        return result.Length == 0 ? raw.Trim() : result;
    }
}
