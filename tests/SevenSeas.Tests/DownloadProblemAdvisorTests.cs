using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class DownloadProblemAdvisorTests
{
    [Theory]
    [InlineData("The downloaded file no longer exists: C:\\temp\\a.zip", null)]
    [InlineData("The downloaded file is empty (0 bytes).", null)]
    [InlineData("Failed to extract 'x': Access to the path is denied.", null)]
    [InlineData("The process cannot access the file because it is being used by another process.", null)]
    [InlineData("Unhandled error 0x800700E1", null)]
    [InlineData(null, "FileAccessDenied")]
    [InlineData(null, "FileFailed")]
    [InlineData("Download interrupted (FileMalicious).", null)]
    [InlineData("The downloaded file for this job is missing.", null)]
    public void Advise_RecognisesAntivirusInterference(string? error, string? reason)
    {
        var advice = DownloadProblemAdvisor.Advise(error, reason);

        Assert.NotNull(advice);
        Assert.True(advice!.IsAntivirusRelated);
        Assert.Contains("antivirus", advice.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advise_RecognisesDiskSpace()
    {
        var advice = DownloadProblemAdvisor.Advise("There is not enough space on the disk.", null);

        Assert.NotNull(advice);
        Assert.Equal(DownloadProblemKind.DiskSpace, advice!.Kind);
    }

    [Fact]
    public void Advise_RecognisesNetworkTrouble()
    {
        var advice = DownloadProblemAdvisor.Advise("The connection was reset by the server.", null);

        Assert.NotNull(advice);
        Assert.Equal(DownloadProblemKind.Network, advice!.Kind);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("The archive contains an unsafe path and was rejected: ../evil.exe", null)]
    public void Advise_StaysQuietWhenThereIsNothingUsefulToSay(string? error, string? reason)
    {
        Assert.Null(DownloadProblemAdvisor.Advise(error, reason));
    }
}

public sealed class AntivirusExclusionsTests
{
    [Fact]
    public void RecommendedFolders_CollapsesToTheSharedParent()
    {
        var root = Path.Combine(Path.GetTempPath(), "sevenseas-root");
        var settings = new AppSettings
        {
            GamesFolder = Path.Combine(root, "Games"),
            TempFolder = Path.Combine(root, "Temp"),
            TrashFolder = Path.Combine(root, "Trash"),
        };

        var folders = AntivirusExclusions.RecommendedFolders(settings);

        Assert.Single(folders);
        Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), folders[0]);
    }

    [Fact]
    public void RecommendedFolders_KeepsThemSeparateWhenTheyDoNotShareAParent()
    {
        var settings = new AppSettings
        {
            GamesFolder = Path.Combine(Path.GetTempPath(), "gv-a", "Games"),
            TempFolder = Path.Combine(Path.GetTempPath(), "gv-b", "Temp"),
            TrashFolder = string.Empty,
        };

        var folders = AntivirusExclusions.RecommendedFolders(settings);

        Assert.Equal(2, folders.Count);
    }

    [Fact]
    public void RecommendedFolders_ReturnsNothingWhenUnconfigured()
    {
        Assert.Empty(AntivirusExclusions.RecommendedFolders(new AppSettings()));
    }
}
