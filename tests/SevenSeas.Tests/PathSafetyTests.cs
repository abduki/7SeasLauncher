using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class PathSafetyTests
{
    [Fact]
    public void IsWithin_AcceptsChildPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "gv-root");

        Assert.True(PathSafety.IsWithin(root, Path.Combine(root, "sub", "file.txt"), out var full));
        Assert.StartsWith(root, full, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsWithin_RejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "gv-root");

        Assert.False(PathSafety.IsWithin(root, Path.Combine(root, "..", "evil.exe"), out _));
    }

    [Fact]
    public void IsWithin_RejectsBlankInputs()
    {
        Assert.False(PathSafety.IsWithin(string.Empty, "x", out _));
        Assert.False(PathSafety.IsWithin("root", string.Empty, out _));
    }

    [Theory]
    [InlineData("../../evil.exe", true)]
    [InlineData("..\\evil.exe", true)]
    [InlineData("/etc/passwd", true)]
    [InlineData("C:evil.exe", true)]
    [InlineData("game\\bin\\game.exe", false)]
    [InlineData("game.exe", false)]
    public void LooksLikeTraversal_DetectsUnsafeKeys(string key, bool expected)
    {
        Assert.Equal(expected, PathSafety.LooksLikeTraversal(key));
    }

    [Fact]
    public void SanitizeFolderName_ReplacesInvalidCharacters()
    {
        var invalid = Path.GetInvalidFileNameChars()[0];
        var result = PathSafety.SanitizeFolderName($"Bad{invalid}Name");

        Assert.DoesNotContain(invalid, result);
        Assert.Contains("Bad", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeFolderName_FallsBackForEmpty(string? input)
    {
        Assert.Equal("Unknown Game", PathSafety.SanitizeFolderName(input));
    }

    [Fact]
    public void SanitizeFolderName_TrimsTrailingDots()
    {
        Assert.Equal("Game", PathSafety.SanitizeFolderName("Game..."));
    }
}
