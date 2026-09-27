using SevenSeas.Core.Models;

namespace SevenSeas.Core.Abstractions;

/// <summary>
/// Reads every .json file in schemes\.
/// </summary>
public interface ISchemeLoader
{
    /// <summary>Directory scanned for scheme files.</summary>
    string SchemesDirectory { get; }

    /// <summary>Loads all valid schemes. Invalid files are skipped and logged, never thrown.</summary>
    IReadOnlyList<SiteScheme> LoadAll();

    SiteScheme? GetByName(string name);

    /// <summary>Discards the cache so the next read picks up files added or removed on disk.</summary>
    void Refresh();

    /// <summary>
    /// Writes a scheme as JSON in <see cref="SchemesDirectory"/> and refreshes the cache.
    /// Pass <paramref name="originalName"/> when renaming so the old file is removed.
    /// </summary>
    SiteScheme Save(SiteScheme scheme, string? originalName = null);

    /// <summary>Deletes the file backing a scheme and refreshes the cache.</summary>
    bool Delete(SiteScheme scheme);
}
