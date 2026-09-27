using System.Text.Json.Serialization;

namespace SevenSeas.Core.Models;

/// <summary>
/// A saved bookmark. On disk it is a scheme file in <c>schemes\</c>,
/// but the UI simply calls them bookmarks — there is no separate "website" list.
/// <para>
/// A bookmark is browsable by virtue of its <see cref="BaseUrl"/>. Supplying a
/// <see cref="SearchUrlTemplate"/> is what additionally makes it <see cref="CanSearch"/>,
/// so "can this be searched?" is a property of a bookmark rather than a category it belongs to.
/// </para>
/// </summary>
public sealed class SiteScheme
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The bookmark's home page — where clicking it and the Home button both go.</summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional search URL pattern containing <c>{query}</c>. Absent for a bookmark that is only
    /// meant to be browsed, which is normal while you are still deciding whether to add a site.
    /// </summary>
    [JsonPropertyName("searchUrlTemplate")]
    public string? SearchUrlTemplate { get; set; }

    [JsonPropertyName("archivePasswordHint")]
    public string? ArchivePasswordHint { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>The file a bookmark was loaded from, for diagnostics and saving.</summary>
    [JsonIgnore]
    public string? SourceFile { get; set; }

    /// <summary>True when this bookmark can be searched, i.e. it has a usable search template.</summary>
    [JsonIgnore]
    public bool CanSearch =>
        !string.IsNullOrWhiteSpace(SearchUrlTemplate) &&
        SearchUrlTemplate.Contains("{query}", StringComparison.Ordinal);

    /// <summary>
    /// True when the bookmark is usable. A missing search template is allowed; a malformed one is not,
    /// because silently ignoring it would make Search appear broken.
    /// </summary>
    [JsonIgnore]
    public bool IsValid => TryValidate(out _);

    /// <summary>Validates the bookmark, returning a human-readable reason when it is unusable.</summary>
    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "A bookmark name is required.";
            return false;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            error = "The address must be an absolute http(s) URL, for example https://example.com.";
            return false;
        }

        // Browse-only bookmarks are fine. A *present* search template must be correct.
        if (string.IsNullOrWhiteSpace(SearchUrlTemplate))
        {
            error = string.Empty;
            return true;
        }

        if (!SearchUrlTemplate.Contains("{query}", StringComparison.Ordinal))
        {
            error = "The search URL must contain {query}, for example https://example.com/search?q={query}. " +
                    "Leave it empty if you only want to browse this bookmark.";
            return false;
        }

        if (!Uri.TryCreate(SearchUrlTemplate.Replace("{query}", "test", StringComparison.Ordinal), UriKind.Absolute, out _))
        {
            error = "The search URL does not form a valid absolute URL once {query} is substituted.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Builds the search URL for a query, or null when this bookmark cannot search.
    /// </summary>
    public string? BuildSearchUrl(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!CanSearch)
        {
            return null;
        }

        return SearchUrlTemplate!.Replace("{query}", Uri.EscapeDataString(query), StringComparison.Ordinal);
    }
}
