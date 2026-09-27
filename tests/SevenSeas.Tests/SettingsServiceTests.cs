using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Load_CreatesDefaultsWhenFileMissing()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Resolve("settings.json");
        var service = new JsonSettingsService(path, null);

        service.Load();

        Assert.True(File.Exists(path));
        Assert.Equal("Dark", service.Current.Theme);
        Assert.False(string.IsNullOrWhiteSpace(service.Current.GamesFolder));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsValues()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Resolve("settings.json");
        var service = new JsonSettingsService(path, null);
        service.Load();
        service.Update(s =>
        {
            s.SteamGridDbApiKey = "abc123";
            s.Theme = "Light";
        });

        var reloaded = new JsonSettingsService(path, null);
        reloaded.Load();

        Assert.Equal("abc123", reloaded.Current.SteamGridDbApiKey);
        Assert.Equal("Light", reloaded.Current.Theme);
    }

    [Fact]
    public void Load_QuarantinesCorruptFileAndUsesDefaults()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateFile("settings.json", "this is not json");
        var service = new JsonSettingsService(path, null);

        service.Load();

        Assert.True(File.Exists(path + ".corrupt"));
        Assert.Equal("Dark", service.Current.Theme);
    }

    [Fact]
    public void Update_RaisesSettingsChanged()
    {
        using var workspace = new TempWorkspace();
        var service = new JsonSettingsService(workspace.Resolve("settings.json"), null);
        service.Load();
        var raised = 0;
        service.SettingsChanged += _ => raised++;

        service.Update(s => s.Theme = "Light");

        Assert.Equal(1, raised);
        Assert.Equal("Light", service.Current.Theme);
    }

    [Fact]
    public void Load_CreatesConfiguredFolders()
    {
        using var workspace = new TempWorkspace();
        var settings = AppSettings.CreateDefault();
        settings.GamesFolder = workspace.Resolve("Games");
        settings.TempFolder = workspace.Resolve("Temp");
        settings.TrashFolder = workspace.Resolve("Trash");

        var path = workspace.Resolve("settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(settings));
        var service = new JsonSettingsService(path, null);

        service.Load();

        Assert.True(Directory.Exists(settings.GamesFolder));
        Assert.True(Directory.Exists(settings.TempFolder));
        Assert.True(Directory.Exists(settings.TrashFolder));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsHomePages()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Resolve("settings.json");
        var service = new JsonSettingsService(path, null);
        service.Load();
        service.Update(s =>
        {
            s.HomePages.Add(new SevenSeas.Core.Models.HomePage { Name = "Search", Url = "https://example.com" });
            s.DefaultHomePageUrl = "https://example.com";
        });

        var reloaded = new JsonSettingsService(path, null);
        reloaded.Load();

        Assert.Single(reloaded.Current.HomePages);
        Assert.Equal("Search", reloaded.Current.HomePages[0].Name);
        Assert.Equal("https://example.com", reloaded.Current.DefaultHomePageUrl);
    }

    [Fact]
    public void Load_ToleratesMissingHomePagesKey()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateFile("settings.json", """{ "theme": "Dark" }""");
        var service = new JsonSettingsService(path, null);

        service.Load();

        Assert.NotNull(service.Current.HomePages);
        Assert.Empty(service.Current.HomePages);
    }

    [Fact]
    public void CreateDefault_UsesExpectedSubfolders()
    {
        var settings = AppSettings.CreateDefault();

        Assert.EndsWith("Games", settings.GamesFolder, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Temp", settings.TempFolder, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Trash", settings.TrashFolder, StringComparison.OrdinalIgnoreCase);
    }
}
