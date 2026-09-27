using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class ExecutableFinderTests
{
    private readonly ExecutableFinder _finder = new();

    private static void WriteFakeExe(string path, int sizeBytes = 2_000_000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
    }

    [Fact]
    public void Find_PrefersExecutableNamedAfterFolder()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Hollow Knight");
        WriteFakeExe(Path.Combine(root, "Hollow Knight.exe"));
        WriteFakeExe(Path.Combine(root, "Other.exe"));

        var result = _finder.Find(root);

        Assert.True(result.Found);
        Assert.EndsWith("Hollow Knight.exe", result.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Find_FiltersInstallerNoise()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Hollow Knight");
        WriteFakeExe(Path.Combine(root, "unins000.exe"));
        WriteFakeExe(Path.Combine(root, "UnityCrashHandler64.exe"));

        var result = _finder.Find(root);

        Assert.False(result.Found);
        Assert.False(result.NeedsUserChoice);
    }

    [Fact]
    public void Find_ReturnsNoneWhenNoExecutables()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Empty");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "hi");

        var result = _finder.Find(root);

        Assert.False(result.Found);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Find_ReturnsNoneForMissingFolder()
    {
        var result = _finder.Find(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid()));

        Assert.False(result.Found);
    }

    [Fact]
    public void Find_AsksUserWhenAmbiguous()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Bundle");
        WriteFakeExe(Path.Combine(root, "Alpha.exe"));
        WriteFakeExe(Path.Combine(root, "Beta.exe"));

        var result = _finder.Find(root);

        Assert.False(result.Found);
        Assert.True(result.NeedsUserChoice);
        Assert.True(result.Candidates.Count >= 2);
    }

    [Fact]
    public void Find_PrefersRootOverNested()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("Hollow Knight");
        WriteFakeExe(Path.Combine(root, "Hollow Knight.exe"));
        WriteFakeExe(Path.Combine(root, "tools", "extra", "helper.exe"));

        var result = _finder.Find(root);

        Assert.True(result.Found);
        Assert.Equal(Path.Combine(root, "Hollow Knight.exe"), result.ExecutablePath);
    }
}
