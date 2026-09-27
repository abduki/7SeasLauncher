using System.Text.Json;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// Reads every .json file in schemes\.
/// A malformed or incomplete scheme is skipped with a warning; it never crashes the app.
/// </summary>
public sealed class SchemeLoader : ISchemeLoader, IArchivePasswordSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<SchemeLoader> _logger;
    private IReadOnlyList<SiteScheme>? _cache;

    public SchemeLoader(string schemesDirectory, ILogger<SchemeLoader>? logger = null)
    {
        SchemesDirectory = schemesDirectory;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SchemeLoader>.Instance;
    }

    public string SchemesDirectory { get; }

    public IReadOnlyList<SiteScheme> LoadAll()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var schemes = new List<SiteScheme>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(SchemesDirectory))
        {
            _logger.LogWarning("Schemes directory {Directory} does not exist.", SchemesDirectory);
            _cache = schemes;
            return _cache;
        }

        foreach (var file in Directory.EnumerateFiles(SchemesDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            SiteScheme? scheme = null;
            try
            {
                var json = File.ReadAllText(file);
                scheme = JsonSerializer.Deserialize<SiteScheme>(json, SerializerOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.LogWarning(ex, "Skipping malformed scheme file {File}.", file);
                continue;
            }

            if (scheme is null)
            {
                _logger.LogWarning("Skipping empty scheme file {File}.", file);
                continue;
            }

            scheme.SourceFile = file;

            if (!scheme.IsValid)
            {
                _logger.LogWarning("Skipping invalid scheme {File}: name/baseUrl/searchUrlTemplate ({Template}) incomplete.",
                    file, scheme.SearchUrlTemplate);
                continue;
            }

            if (!seenNames.Add(scheme.Name))
            {
                _logger.LogWarning("Skipping duplicate scheme name '{Name}' in {File}.", scheme.Name, file);
                continue;
            }

            schemes.Add(scheme);
            _logger.LogInformation("Loaded scheme '{Name}' from {File}.", scheme.Name, file);
        }

        _cache = schemes;
        return _cache;
    }

    public SiteScheme? GetByName(string name)
        => LoadAll().FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Re-reads schemes from disk, discarding the cache.</summary>
    public void Refresh() => _cache = null;

    public SiteScheme Save(SiteScheme scheme, string? originalName = null)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        if (!scheme.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(scheme));
        }

        Directory.CreateDirectory(SchemesDirectory);

        var target = Path.Combine(SchemesDirectory, Slugify(scheme.Name) + ".json");

        // Renaming a site must not leave the old file behind.
        if (!string.IsNullOrWhiteSpace(originalName) &&
            !string.Equals(originalName, scheme.Name, StringComparison.OrdinalIgnoreCase))
        {
            var previous = FindFileForName(originalName);
            if (previous is not null && !string.Equals(previous, target, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(previous);
            }
        }

        File.WriteAllText(target, JsonSerializer.Serialize(scheme, WriteOptions));
        scheme.SourceFile = target;
        _logger.LogInformation("Saved scheme '{Name}' to {File}.", scheme.Name, target);
        Refresh();
        return scheme;
    }

    public bool Delete(SiteScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        var path = !string.IsNullOrWhiteSpace(scheme.SourceFile) && File.Exists(scheme.SourceFile)
            ? scheme.SourceFile
            : FindFileForName(scheme.Name);

        if (path is null || !File.Exists(path))
        {
            return false;
        }

        TryDelete(path);
        _logger.LogInformation("Deleted scheme '{Name}' ({File}).", scheme.Name, path);
        Refresh();
        return true;
    }

    private string? FindFileForName(string name)
    {
        if (!Directory.Exists(SchemesDirectory))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(SchemesDirectory, "*.json"))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<SiteScheme>(File.ReadAllText(file), SerializerOptions);
                if (parsed is not null &&
                    string.Equals(parsed.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Unreadable file: skip.
            }
        }

        return null;
    }

    private static string Slugify(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "scheme" : slug;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete scheme file {File}.", path);
        }
    }

    public string? GetPasswordHint(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) ||
            !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return LoadAll()
            .FirstOrDefault(s =>
                Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var baseUri) &&
                string.Equals(baseUri.Host, uri.Host, StringComparison.OrdinalIgnoreCase))
            ?.ArchivePasswordHint;
    }
}
