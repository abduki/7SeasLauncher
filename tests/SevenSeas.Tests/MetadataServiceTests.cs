using System.Net;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class MetadataServiceTests
{
    private const string SearchJson = """
    { "success": true, "data": [ { "id": 42, "name": "Hollow Knight" } ] }
    """;

    private const string GridsJson = """
    { "success": true, "data": [ { "url": "https://cdn.steamgriddb.test/hk.png" } ] }
    """;

    private static MetadataService Create(StubHttpMessageHandler handler, string apiKey)
    {
        var settings = new FakeSettingsService();
        settings.Update(s => s.SteamGridDbApiKey = apiKey);
        return new MetadataService(new HttpClient(handler), settings);
    }

    [Fact]
    public async Task Lookup_ReturnsTitleAndCover()
    {
        var handler = new StubHttpMessageHandler()
            .Route("search/autocomplete/", SearchJson)
            .Route("grids/game/42", GridsJson);
        var service = Create(handler, "key");

        var result = await service.LookupAsync("Hollow.Knight.v1.5.zip");

        Assert.Equal("Hollow Knight", result.Title);
        Assert.Equal("https://cdn.steamgriddb.test/hk.png", result.CoverUrl);
    }

    [Fact]
    public async Task Lookup_SendsBearerToken()
    {
        string? auth = null;
        var handler = new StubHttpMessageHandler()
            .Route("search/autocomplete/", request =>
            {
                auth = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchJson) };
            })
            .Route("grids/game/42", GridsJson);
        var service = Create(handler, "my-key");

        await service.LookupAsync("Hollow Knight");

        Assert.Equal("Bearer my-key", auth);
    }

    [Fact]
    public async Task Lookup_NoResults_FallsBackToCleanedFilename()
    {
        var handler = new StubHttpMessageHandler()
            .Route("search/autocomplete/", """{ "success": true, "data": [] }""");
        var service = Create(handler, "key");

        var result = await service.LookupAsync("Hollow.Knight.v1.5.zip");

        Assert.Equal("Hollow Knight", result.Title);
        Assert.Null(result.CoverUrl);
    }

    [Fact]
    public async Task Lookup_ServerError_FallsBack()
    {
        var handler = new StubHttpMessageHandler()
            .Route("search/autocomplete/", "{}", HttpStatusCode.InternalServerError);
        var service = Create(handler, "key");

        var result = await service.LookupAsync("Celeste.zip");

        Assert.Equal("Celeste", result.Title);
    }

    [Fact]
    public async Task Lookup_WithoutApiKey_DoesNotCallNetwork()
    {
        var handler = new StubHttpMessageHandler().Route("search/autocomplete/", SearchJson);
        var service = Create(handler, string.Empty);

        var result = await service.LookupAsync("Hades.zip");

        Assert.Equal("Hades", result.Title);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Lookup_MalformedJson_FallsBack()
    {
        var handler = new StubHttpMessageHandler().Route("search/autocomplete/", "not json at all");
        var service = Create(handler, "key");

        var result = await service.LookupAsync("Celeste.zip");

        Assert.Equal("Celeste", result.Title);
    }

    [Fact]
    public async Task Lookup_CoverLookupFails_StillReturnsTitle()
    {
        var handler = new StubHttpMessageHandler()
            .Route("search/autocomplete/", SearchJson)
            .Route("grids/game/42", "{}", HttpStatusCode.NotFound);
        var service = Create(handler, "key");

        var result = await service.LookupAsync("Hollow Knight");

        Assert.Equal("Hollow Knight", result.Title);
        Assert.Null(result.CoverUrl);
    }
}
