using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class DownloadIntakeServiceTests
{
    private static (DownloadIntakeService Intake, SqliteGameRepository Repo) Create(TempWorkspace workspace)
    {
        var repo = new SqliteGameRepository(workspace.Resolve("7seas.db"));
        repo.Initialize();
        return (new DownloadIntakeService(repo, new FakeClock()), repo);
    }

    [Fact]
    public void BeginDownload_CreatesDownloadingJobAndRaisesEvent()
    {
        using var workspace = new TempWorkspace();
        var (intake, repo) = Create(workspace);
        Job? observed = null;
        intake.JobChanged += j => observed = j;

        var job = intake.BeginDownload("Hollow.Knight.v1.5.zip", "https://example.test/game");

        Assert.Equal(JobState.Downloading, job.State);
        Assert.Equal(JobState.Downloading, repo.Get(job.Id)!.State);
        Assert.NotNull(observed);
        Assert.Equal(job.Id, observed!.Id);
    }

    [Fact]
    public void BeginDownload_DefaultsBlankName()
    {
        using var workspace = new TempWorkspace();
        var (intake, _) = Create(workspace);

        Assert.Equal("Unknown Game", intake.BeginDownload("   ", null).GameName);
    }

    [Fact]
    public void AttachDownloadPath_RecordsPath()
    {
        using var workspace = new TempWorkspace();
        var (intake, repo) = Create(workspace);
        var job = intake.BeginDownload("Game", null);

        intake.AttachDownloadPath(job.Id, @"C:\Temp\downloads\x.zip");

        Assert.Equal(@"C:\Temp\downloads\x.zip", repo.Get(job.Id)!.DownloadedPath);
    }

    [Fact]
    public void MarkDownloadCompleted_QueuesJob()
    {
        using var workspace = new TempWorkspace();
        var (intake, repo) = Create(workspace);
        var job = intake.BeginDownload("Game", null);

        intake.MarkDownloadCompleted(job.Id, @"C:\Temp\x.zip");

        var loaded = repo.Get(job.Id)!;
        Assert.Equal(JobState.Queued, loaded.State);
        Assert.Equal(@"C:\Temp\x.zip", loaded.DownloadedPath);
    }

    [Fact]
    public void MarkDownloadFailed_SetsFailedWithReason()
    {
        using var workspace = new TempWorkspace();
        var (intake, repo) = Create(workspace);
        var job = intake.BeginDownload("Game", null);

        intake.MarkDownloadFailed(job.Id, "network reset");

        var loaded = repo.Get(job.Id)!;
        Assert.Equal(JobState.Failed, loaded.State);
        Assert.Equal("network reset", loaded.LastError);
    }

    [Fact]
    public void GetActiveJobs_ExcludesTerminalJobs()
    {
        using var workspace = new TempWorkspace();
        var (intake, _) = Create(workspace);
        var active = intake.BeginDownload("A", null);
        var finished = intake.BeginDownload("B", null);
        intake.MarkDownloadFailed(finished.Id, "nope");

        var jobs = intake.GetActiveJobs();

        Assert.Single(jobs);
        Assert.Equal(active.Id, jobs[0].Id);
    }

    [Fact]
    public void MarkDownloadCompleted_UnknownJob_ReturnsNull()
    {
        using var workspace = new TempWorkspace();
        var (intake, _) = Create(workspace);

        Assert.Null(intake.MarkDownloadCompleted("missing"));
        Assert.Null(intake.MarkDownloadFailed("missing", "x"));
    }
}
