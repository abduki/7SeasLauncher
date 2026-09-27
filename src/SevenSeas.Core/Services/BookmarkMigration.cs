using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// One-time move of the retired "home pages" list from settings.json into browse-only bookmark files,
/// so upgrading does not lose anything.
/// </summary>
public static class BookmarkMigration
{
    /// <summary>
    /// Creates a bookmark file for every saved home page that does not already exist as one.
    /// Entries are only removed from settings once they are safely on disk.
    /// </summary>
    /// <returns>The number of home pages converted into bookmarks.</returns>
    public static int Migrate(ISettingsService settings, ISchemeLoader schemes, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(schemes);

        var pages = settings.Current.HomePages;
        if (pages.Count == 0)
        {
            return 0;
        }

        var known = schemes.LoadAll()
            .Select(s => Normalize(s.BaseUrl))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stillPending = new List<HomePage>();
        var migrated = 0;

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Url))
            {
                continue;
            }

            if (known.Contains(Normalize(page.Url)))
            {
                // Already present as a bookmark; nothing to write.
                migrated++;
                continue;
            }

            var name = string.IsNullOrWhiteSpace(page.Name) ? SiteNaming.FromUrl(page.Url) : page.Name;

            try
            {
                schemes.Save(new SiteScheme
                {
                    Name = name,
                    BaseUrl = page.Url,
                    SearchUrlTemplate = null,
                    Notes = "Saved before bookmarks and websites were unified.",
                });

                known.Add(Normalize(page.Url));
                migrated++;
            }
            catch (Exception ex)
            {
                // Never drop data we could not write.
                logger?.LogWarning(ex, "Could not migrate home page {Url}; leaving it in settings.", page.Url);
                stillPending.Add(page);
            }
        }

        settings.Update(s => s.HomePages = stillPending);

        if (migrated > 0)
        {
            logger?.LogInformation("Migrated {Count} home page(s) into bookmarks.", migrated);
        }

        return migrated;
    }

    private static string Normalize(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');
}
