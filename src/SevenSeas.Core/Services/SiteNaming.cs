namespace SevenSeas.Core.Services;

/// <summary>
/// Derives a sensible scheme name from the page the user is looking at, so adding a site
/// needs no typing.
/// </summary>
public static class SiteNaming
{
    private static readonly string[] TitleSeparators = { " | ", " - ", " – ", " — ", " · ", " :: " };

    private static readonly string[] UnhelpfulTitles =
    {
        "search", "results", "error", "404", "not found", "forbidden", "access denied",
        "just a moment", "attention required", "index of", "loading", "redirecting",
        "sign in", "log in", "login", "register",
    };

    /// <summary>Turns a URL's host into a readable brand, e.g. https://www.animex.one/x -> "Animex".</summary>
    public static string FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "New Site";
        }

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        var label = host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(label))
        {
            return "New Site";
        }

        var words = label
            .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .ToArray();

        return words.Length == 0 ? "New Site" : string.Join(' ', words);
    }

    /// <summary>
    /// Prefers the leading part of the page title when it looks like a site name,
    /// otherwise falls back to the host-derived brand.
    /// </summary>
    public static string DisplayName(string? pageTitle, string? url)
    {
        var fallback = FromUrl(url);

        if (string.IsNullOrWhiteSpace(pageTitle))
        {
            return fallback;
        }

        var candidate = pageTitle.Trim();
        foreach (var separator in TitleSeparators)
        {
            var index = candidate.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
            {
                candidate = candidate[..index].Trim();
                break;
            }
        }

        if (candidate.Length is < 2 or > 40)
        {
            return fallback;
        }

        // Browser error pages title themselves with the bare host, e.g. "example-game-site.test".
        if (candidate.Contains('.') && !candidate.Contains(' '))
        {
            return fallback;
        }

        if (UnhelpfulTitles.Any(word => candidate.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return fallback;
        }

        return candidate;
    }

    /// <summary>
    /// Builds a search URL template from a discovered search form, e.g.
    /// ("https://site.test/find", "q") -> "https://site.test/find?q={query}".
    /// </summary>
    public static string BuildSearchTemplate(string? actionUrl, string? parameterName)
    {
        var parameter = string.IsNullOrWhiteSpace(parameterName) ? "q" : parameterName.Trim();
        var action = string.IsNullOrWhiteSpace(actionUrl) ? string.Empty : actionUrl.Trim();

        if (action.Length == 0)
        {
            return string.Empty;
        }

        var separator = action.Contains('?') ? '&' : '?';
        return $"{action}{separator}{parameter}={{query}}";
    }

    /// <summary>Reduces a URL to its site root, used by the Home button.</summary>
    public static string? SiteRoot(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }

    /// <summary>
    /// True for addresses that cannot be real: the reserved example/test TLDs and localhost.
    /// The shipped sample bookmark uses one, and it should never be what a new user lands on.
    /// </summary>
    public static bool IsPlaceholderAddress(string? url)
    {
        var host = NormalizedHost(url);
        if (host is null)
        {
            return false;
        }

        return host == "localhost"
            || host.EndsWith(".test", StringComparison.Ordinal)
            || host.EndsWith(".invalid", StringComparison.Ordinal)
            || host.EndsWith(".example", StringComparison.Ordinal)
            || host.EndsWith(".local", StringComparison.Ordinal);
    }

    /// <summary>Lower-cased host with a leading "www." removed, or null when unusable.</summary>
    public static string? NormalizedHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// Decides where the Home button should go.
    /// <para>
    /// If the page being viewed belongs to one of the user's registered websites, Home goes to that
    /// website's own home page (<c>baseUrl</c>), because a site's real home is often not its bare origin.
    /// Otherwise it goes to the current site's root.
    /// </para>
    /// A bookmark is only ever a way to reach a page, so its full URL is never returned here —
    /// being on a bookmarked page always resolves to the website's home.
    /// </summary>
    public static string? ResolveHomeUrl(
        string? currentUrl,
        IEnumerable<string>? registeredHomeUrls,
        string? fallbackHomeUrl)
    {
        var host = NormalizedHost(currentUrl);

        if (host is not null && registeredHomeUrls is not null)
        {
            foreach (var home in registeredHomeUrls)
            {
                if (!string.IsNullOrWhiteSpace(home) &&
                    string.Equals(NormalizedHost(home), host, StringComparison.OrdinalIgnoreCase))
                {
                    return home;
                }
            }
        }

        var root = SiteRoot(currentUrl);
        if (root is not null)
        {
            return root;
        }

        return string.IsNullOrWhiteSpace(fallbackHomeUrl) ? null : fallbackHomeUrl;
    }

    private static string TitleCase(string value)
        => string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
