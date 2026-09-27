using System.IO.Compression;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class ArchiveExtractorTests
{
    private readonly ArchiveExtractor _extractor = new();

    private static string CreateZip(string path, params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public async Task Extract_UnpacksZipIntoDestination()
    {
        using var workspace = new TempWorkspace();
        var zip = CreateZip(workspace.Resolve("game.zip"), ("data/hello.txt", "hi"), ("readme.txt", "read me"));
        var destination = workspace.Resolve("Games/Game");

        var result = await _extractor.ExtractAsync(zip, destination, null);

        Assert.True(result.Success, result.Error);
        Assert.Equal("hi", File.ReadAllText(Path.Combine(destination, "data", "hello.txt")));
        Assert.Equal("read me", File.ReadAllText(Path.Combine(destination, "readme.txt")));
    }

    [Fact]
    public async Task Extract_RejectsTraversalEntry()
    {
        using var workspace = new TempWorkspace();
        var zip = CreateZip(workspace.Resolve("evil.zip"), ("../evil.txt", "pwned"));
        var destination = workspace.Resolve("Games/Game");

        var result = await _extractor.ExtractAsync(zip, destination, null);

        Assert.False(result.Success);
        Assert.Contains("unsafe path", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(workspace.Resolve("Games/evil.txt")));
    }

    [Fact]
    public async Task Extract_MissingArchive_Fails()
    {
        using var workspace = new TempWorkspace();

        var result = await _extractor.ExtractAsync(workspace.Resolve("nope.zip"), workspace.Resolve("out"), null);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extract_BlankDestination_Fails()
    {
        using var workspace = new TempWorkspace();
        var zip = CreateZip(workspace.Resolve("a.zip"), ("a.txt", "a"));

        var result = await _extractor.ExtractAsync(zip, string.Empty, null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Extract_NonArchive_Fails()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.CreateFile("fake.zip", "definitely not a zip archive at all");

        var result = await _extractor.ExtractAsync(file, workspace.Resolve("out"), null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Extract_IsIdempotent()
    {
        using var workspace = new TempWorkspace();
        var zip = CreateZip(workspace.Resolve("game.zip"), ("a.txt", "one"));
        var destination = workspace.Resolve("Games/Game");

        var first = await _extractor.ExtractAsync(zip, destination, null);
        var second = await _extractor.ExtractAsync(zip, destination, null);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("one", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }
}
