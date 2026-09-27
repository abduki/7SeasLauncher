using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class TrashServiceTests
{
    [Fact]
    public void MoveToTrash_MovesFileKeepingExtension()
    {
        using var workspace = new TempWorkspace();
        var settings = new JsonSettingsService(workspace.Resolve("settings.json"), null);
        settings.Load();
        settings.Update(s => s.TrashFolder = workspace.Resolve("Trash"));
        var source = workspace.CreateFile("downloads/abc.zip", new byte[] { 1, 2, 3 });

        var result = new TrashService(settings).MoveToTrash(source, "abc");

        Assert.NotNull(result);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(result!));
        Assert.EndsWith("abc.zip", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MoveToTrash_MissingFile_ReturnsNull()
    {
        using var workspace = new TempWorkspace();
        var settings = new JsonSettingsService(workspace.Resolve("settings.json"), null);
        settings.Load();

        Assert.Null(new TrashService(settings).MoveToTrash(workspace.Resolve("nope.zip"), "id"));
        Assert.Null(new TrashService(settings).MoveToTrash(null, "id"));
    }

    [Fact]
    public void CleanupExpired_RemovesOnlyOldEntries()
    {
        using var workspace = new TempWorkspace();
        var settings = new JsonSettingsService(workspace.Resolve("settings.json"), null);
        settings.Load();
        var trash = workspace.CreateDirectory("Trash");
        settings.Update(s =>
        {
            s.TrashFolder = trash;
            s.TrashRetentionDays = 7;
        });

        var oldFile = workspace.CreateFile("Trash/old.zip", "x");
        var newFile = workspace.CreateFile("Trash/new.zip", "y");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-30));

        var removed = new TrashService(settings).CleanupExpired();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void CleanupExpired_MissingFolder_ReturnsZero()
    {
        using var workspace = new TempWorkspace();
        var settings = new JsonSettingsService(workspace.Resolve("settings.json"), null);
        settings.Load();
        settings.Update(s => s.TrashFolder = workspace.Resolve("no-trash"));

        Assert.Equal(0, new TrashService(settings).CleanupExpired());
    }
}
