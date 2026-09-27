using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

/// <summary>
/// Opt-in live checks against SteamGridDB. They only run when SEVENSEAS_TEST_SGDB_KEY is set,
/// so the default test run stays offline and never burns API quota.
/// </summary>
public sealed class MetadataIntegrationTests
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("SEVENSEAS_TEST_SGDB_KEY");

    [Fact]
    public async Task LiveLookup_ReturnsTitleAndCoverArt()
    {
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var settings = new FakeSettingsService();
        settings.Update(s => s.SteamGridDbApiKey = key);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var service = new MetadataService(http, settings);

        var result = await service.LookupAsync("Hollow.Knight.v1.5.zip");

        Assert.False(string.IsNullOrWhiteSpace(result.Title));
        Assert.False(string.IsNullOrWhiteSpace(result.CoverUrl));
        Assert.StartsWith("https://", result.CoverUrl, StringComparison.OrdinalIgnoreCase);
    }
}
