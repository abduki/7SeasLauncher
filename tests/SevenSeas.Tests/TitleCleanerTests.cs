using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class TitleCleanerTests
{
    [Theory]
    [InlineData("Hollow.Knight.v1.5.zip", "Hollow Knight")]
    [InlineData("Hollow_Knight_[FitGirl Repack].rar", "Hollow Knight")]
    [InlineData("Celeste (2018).7z", "Celeste")]
    [InlineData("C:\\downloads\\Hades.exe", "Hades")]
    public void Clean_StripsNoiseTokens(string input, string expected)
    {
        Assert.Equal(expected, TitleCleaner.Clean(input));
    }

    [Fact]
    public void Clean_FallsBackToRawWhenEverythingIsNoise()
    {
        var result = TitleCleaner.Clean("repack.setup.iso");
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void Clean_HandlesNullAndEmpty()
    {
        Assert.Equal(string.Empty, TitleCleaner.Clean(null));
        Assert.Equal(string.Empty, TitleCleaner.Clean("  "));
    }
}
