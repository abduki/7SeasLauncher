using System.Text.Json;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;
using Polly;

namespace SevenSeas.Core.Services;

/// <summary>
/// Resolves title + cover art from SteamGridDB.
/// Any failure falls back to the cleaned filename and a null cover; it never blocks the pipeline.
/// </summary>
public sealed class MetadataService : IMetadataService
{
    /// <summary>SteamGridDB REST root. Absolute URIs keep the client independent of HttpClient.BaseAddress.</summary>
    public const string ApiBaseUrl = "https://www.steamgriddb.com/api/v2/";

    private const string SearchEndpoint = "search/autocomplete/";
    private const string GridsEndpoint = "grids/game/";

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILogger<MetadataService> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;

    public MetadataService(
        HttpClient http,
        ISettingsService settings,
        ILogger<MetadataService>? logger = null,
        IAsyncPolicy<HttpResponseMessage>? retryPolicy = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MetadataService>.Instance;
        _retryPolicy = retryPolicy ?? Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
    }

    public async Task<GameMetadata> LookupAsync(string fileOrGameName, CancellationToken cancellationToken = default)
    {
        var cleaned = TitleCleaner.Clean(fileOrGameName);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = fileOrGameName?.Trim() ?? "Unknown Game";
        }

        var apiKey = _settings.Current.SteamGridDbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("No SteamGridDB API key configured; using filename '{Title}'.", cleaned);
            return GameMetadata.Fallback(cleaned);
        }

        try
        {
            var match = await SearchAsync(cleaned, apiKey, cancellationToken).ConfigureAwait(false);
            if (match is null)
            {
                _logger.LogInformation("SteamGridDB had no match for '{Query}'.", cleaned);
                return GameMetadata.Fallback(cleaned);
            }

            var cover = await GetCoverAsync(match.Value.Id, apiKey, cancellationToken).ConfigureAwait(false);
            var title = string.IsNullOrWhiteSpace(match.Value.Name) ? cleaned : match.Value.Name;
            _logger.LogInformation("SteamGridDB matched '{Query}' -> '{Title}' (id {Id}, cover {Cover}).",
                cleaned, title, match.Value.Id, cover ?? "none");
            return new GameMetadata(title, cover, Matched: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "SteamGridDB lookup failed for '{Query}'; using filename fallback.", cleaned);
            return GameMetadata.Fallback(cleaned);
        }
    }

    /// <summary>
    /// Searches SteamGridDB and prefers the closest title match rather than blindly taking the first hit
    /// (autocomplete ranks 'Hollow Knight: Silksong' alongside 'Hollow Knight').
    /// </summary>
    private async Task<(int Id, string? Name)?> SearchAsync(string query, string apiKey, CancellationToken cancellationToken)
    {
        var url = ApiBaseUrl + SearchEndpoint + Uri.EscapeDataString(query);
        using var response = await _retryPolicy
            .ExecuteAsync(async () =>
            {
                // A new request per attempt: HttpRequestMessage cannot be re-sent.
                using var request = CreateRequest(url, apiKey);
                return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SteamGridDB search returned HTTP {Status}.", (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() == 0)
        {
            return null;
        }

        var candidates = new List<(int Id, string? Name)>();
        foreach (var element in data.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
            {
                var name = element.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                candidates.Add((id, name));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var wanted = Normalize(query);
        var exact = candidates.FirstOrDefault(c => c.Name is not null && Normalize(c.Name) == wanted);
        if (exact.Name is not null)
        {
            return exact;
        }

        var startsWith = candidates.FirstOrDefault(c =>
            c.Name is not null && Normalize(c.Name).StartsWith(wanted, StringComparison.Ordinal));
        return startsWith.Name is not null ? startsWith : candidates[0];
    }

    /// <summary>
    /// Fetches a cover-art URL. No 'dimensions' filter is sent: SteamGridDB rejects unknown size
    /// combinations with HTTP 400 ("Invalid asset dimensions specified"), which previously broke every lookup.
    /// </summary>
    private async Task<string?> GetCoverAsync(int gameId, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var requestUrl = $"{ApiBaseUrl}{GridsEndpoint}{gameId}";
            using var response = await _retryPolicy
                .ExecuteAsync(async () =>
                {
                    using var request = CreateRequest(requestUrl, apiKey);
                    return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                })
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cover-art lookup for game {GameId} returned HTTP {Status}.", gameId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var portraits = new List<string>();
            var others = new List<string>();
            foreach (var element in data.EnumerateArray())
            {
                if (!element.TryGetProperty("url", out var urlElement))
                {
                    continue;
                }

                var url = urlElement.GetString();
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var width = element.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                var height = element.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                (height > width ? portraits : others).Add(url);
            }

            return portraits.FirstOrDefault() ?? others.FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Cover-art lookup failed for game {GameId}.", gameId);
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(string url, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
