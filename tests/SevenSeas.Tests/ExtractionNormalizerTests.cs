using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class ExtractionNormalizerTests
{
    [Theory]
    [InlineData("Fix", true)]
    [InlineData("Repair", true)]
    [InlineData("Crack", true)]
    [InlineData("CODEX", true)]
    [InlineData("Fix.Repair", true)]
    [InlineData("Slow Roads", false)]
    [InlineData("bin", false)]
    public void LooksLikeFixFolder_RecognisesTheUsualSuspects(string name, bool expected)
    {
        Assert.Equal(expected, ExtractionNormalizer.LooksLikeFixFolder(name));
    }

    [Fact]
    public void Normalize_UnwrapsASingleWrapperFolder()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Game");
        workspace.CreateFile("Game/Slow Roads/Slow Roads.exe", "MZ");

        var result = ExtractionNormalizer.Normalize(root);

        Assert.True(result.Flattened);
        Assert.True(File.Exists(Path.Combine(root, "Slow Roads.exe")));
    }

    [Fact]
    public void Normalize_MergesAFixFolderOverTheGame()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Game");
        workspace.CreateFile("Game/TheGame/TheGame.exe", "original");
        workspace.CreateFile("Game/TheGame/data/shared.bin", "old");
        workspace.CreateFile("Game/Fix/TheGame.exe", "cracked");
        workspace.CreateFile("Game/Fix/extra.dll", "new");

        var result = ExtractionNormalizer.Normalize(root);

        Assert.Equal(1, result.FixFoldersMerged);
        Assert.False(Directory.Exists(Path.Combine(root, "Fix")));

        var gameFolder = Path.Combine(root, "TheGame");
        Assert.Equal("cracked", File.ReadAllText(Path.Combine(gameFolder, "TheGame.exe")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(gameFolder, "extra.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(gameFolder, "data", "shared.bin")));
    }

    [Fact]
    public void Normalize_LeavesAnOrdinaryFolderAlone()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Game");
        workspace.CreateFile("Game/game.exe", "MZ");
        workspace.CreateFile("Game/data/pack.bin", "x");

        var result = ExtractionNormalizer.Normalize(root);

        Assert.False(result.Flattened);
        Assert.Equal(0, result.FixFoldersMerged);
    }

    [Fact]
    public void Normalize_MissingFolderIsHarmless()
    {
        Assert.Equal(0, ExtractionNormalizer.Normalize(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid())).FixFoldersMerged);
    }
}

public sealed class SpaceCleanupServiceTests
{
    [Fact]
    public void CleanEverything_RemovesTrashAndTempDownloadsAndReportsTheSize()
    {
        using var workspace = new TempWorkspace();
        var settings = new FakeSettingsService();
        settings.Update(s =>
        {
            s.TrashFolder = workspace.CreateDirectory("Trash");
            s.TempFolder = workspace.CreateDirectory("Temp");
        });

        workspace.CreateFile("Trash/a.zip", new string('x', 2048));
        workspace.CreateFile("Temp/downloads/b.zip", new string('y', 1024));

        var service = new SpaceCleanupService(settings);
        Assert.Equal(3072, service.ReclaimableBytes());

        var result = service.CleanEverything();

        Assert.Equal(2, result.FilesDeleted);
        Assert.Equal(3072, result.BytesFreed);
        Assert.Equal(0, service.ReclaimableBytes());
    }

    [Fact]
    public void ClearTrash_OnAnEmptyFolderReportsNothing()
    {
        using var workspace = new TempWorkspace();
        var settings = new FakeSettingsService();
        settings.Update(s => s.TrashFolder = workspace.CreateDirectory("Trash"));

        Assert.Equal(0, new SpaceCleanupService(settings).ClearTrash().FilesDeleted);
    }
}
