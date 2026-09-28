using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace SevenSeas.Launcher.Services;

/// <summary>
/// Installs the ad blocker extension into the WebView2 profile.
///
/// Three things shape this class. First, WebView2 can only load an extension from an unpacked
/// folder on disk - there is no store integration - so the build is fetched once and kept under
/// the app's data folder. Second, the extension is referenced by path rather than copied into the
/// profile, so a newer build has to land in a new folder; editing one in place uninstalls it.
/// Third, uBlock Origin Lite ships with only a handful of its rulesets enabled and WebView2 offers
/// no extension UI to switch the rest on, so the set is chosen here instead.
/// </summary>
public static class AdBlocker
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/uBlockOrigin/uBOL-home/releases/latest";

    /// <summary>
    /// Roughly uBO Lite's practical maximum. Its manifest ships 56 rulesets with six enabled;
    /// turning on every one exceeds the static rules budget Chromium allows per extension and the
    /// install fails outright. This set is the default block lists plus the annoyances and privacy
    /// lists, plus one large regional list, which lands just inside the limit.
    /// </summary>
    private static readonly HashSet<string> EnabledRulesets = new(StringComparer.Ordinal)
    {
        "ublock-filters", "easylist", "easyprivacy", "pgl", "ublock-badware", "urlhaus-full",
        "annoyances-ai", "annoyances-cookies", "annoyances-overlays", "annoyances-social",
        "annoyances-widgets", "annoyances-others", "annoyances-notifications",
        "adguard-spyware-url", "rus-0",
    };

    private static readonly HttpClient Downloader = CreateDownloader();

    /// <summary>
    /// Makes sure the extension is installed, downloading a copy the first time. Returns the
    /// extension name, or null when there is nothing to install and nothing could be fetched.
    /// </summary>
    public static async Task<string?> EnsureInstalledAsync(CoreWebView2 core, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(core);

        var root = AppPaths.ExtensionsFolder;
        Directory.CreateDirectory(root);

        var source = LatestDownloaded(root) ?? await DownloadAsync(root, logger);
        if (source is null)
        {
            return null;
        }

        var folder = EnsureTunedCopy(source, logger);
        var marker = Path.Combine(root, ".installed");

        var installed = await core.Profile.GetBrowserExtensionsAsync();
        var current = installed.FirstOrDefault(
            e => e.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase));

        // The profile keeps pointing at whichever folder was installed first, so a newer build only
        // takes effect if the old registration is dropped first.
        var recorded = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        if (current is not null && string.Equals(recorded, folder, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Ad blocker already present: {Name}", current.Name);
            return current.Name;
        }

        if (current is not null)
        {
            await current.RemoveAsync();
            logger.LogInformation("Removed the previous ad blocker build.");
        }

        var added = await core.Profile.AddBrowserExtensionAsync(folder);
        File.WriteAllText(marker, folder);
        logger.LogInformation("Ad blocker installed: {Name}", added.Name);
        return added.Name;
    }

    /// <summary>The most recent build already on disk, or null when there is none.</summary>
    private static string? LatestDownloaded(string root)
        => Directory
            .GetDirectories(root, "uBOL-*")
            .Where(d => !d.EndsWith("-tuned", StringComparison.Ordinal))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// Fetches the newest uBlock Origin Lite build. It is downloaded rather than shipped so the
    /// release carries no GPL code, which also means the extension can be refreshed later.
    /// </summary>
    private static async Task<string?> DownloadAsync(string root, ILogger logger)
    {
        var staging = Path.Combine(Path.GetTempPath(), "7seas-adblock-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var response = await Downloader.GetAsync(LatestReleaseApi).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            var release = document.RootElement;
            var tag = release.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;

            var downloadUrl = FindChromiumAsset(release);

            if (string.IsNullOrWhiteSpace(tag) || downloadUrl is null)
            {
                logger.LogWarning("The ad blocker release did not contain a Chromium build.");
                return null;
            }

            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, "ubol.zip");

            using (var stream = await Downloader.GetStreamAsync(downloadUrl).ConfigureAwait(false))
            using (var file = File.Create(archive))
            {
                await stream.CopyToAsync(file).ConfigureAwait(false);
            }

            var destination = Path.Combine(root, "uBOL-" + tag);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            ZipFile.ExtractToDirectory(archive, destination);
            logger.LogInformation("Downloaded the ad blocker build {Tag}.", tag);
            return destination;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or UnauthorizedAccessException)
        {
            // No ad blocker is better than no browser: the caller carries on either way.
            logger.LogWarning(ex, "Could not download the ad blocker.");
            return null;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private static string? FindChromiumAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (name is null || !name.EndsWith(".chromium.zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
        }

        return null;
    }

    /// <summary>
    /// Produces the tuned build next to the downloaded one, once. Not reused for edits later:
    /// the profile references this folder, so rewriting it would remove the extension.
    /// </summary>
    private static string EnsureTunedCopy(string source, ILogger logger)
    {
        var parent = Path.GetDirectoryName(source);
        if (parent is null)
        {
            return source;
        }

        var tuned = Path.Combine(parent, Path.GetFileName(source) + "-tuned");
        if (Directory.Exists(tuned))
        {
            return tuned;
        }

        CopyDirectory(source, tuned);

        var manifestPath = Path.Combine(tuned, "manifest.json");
        var manifest = File.ReadAllText(manifestPath);

        manifest = Regex.Replace(
            manifest,
            "\"id\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"enabled\"\\s*:\\s*(?:true|false)",
            match =>
            {
                var id = match.Groups[1].Value;
                var enabled = EnabledRulesets.Contains(id) ? "true" : "false";
                return "\"id\": \"" + id + "\", \"enabled\": " + enabled;
            });

        File.WriteAllText(manifestPath, manifest);
        logger.LogInformation(
            "Ad blocker tuned: {Enabled} of the rulesets enabled.", EnabledRulesets.Count);

        return tuned;
    }

    private static HttpClient CreateDownloader()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // The GitHub API rejects requests without a user agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("7SeasLauncher");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Temp files are not worth failing over.
        }
    }
}
