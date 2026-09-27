using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class RepositoryTests
{
    private static SqliteGameRepository Create(TempWorkspace workspace)
    {
        var repo = new SqliteGameRepository(workspace.Resolve("7seas.db"));
        repo.Initialize();
        return repo;
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using var workspace = new TempWorkspace();
        var repo = new SqliteGameRepository(workspace.Resolve("7seas.db"));

        repo.Initialize();
        repo.Initialize();

        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public void CreateAndGet_RoundTripsJob()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        var job = new Job
        {
            Id = "job-1",
            GameName = "Hollow Knight",
            SourceUrl = "https://example.test/1",
            DownloadedPath = @"C:\Temp\1.zip",
            State = JobState.Queued,
            CreatedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
        };

        repo.Create(job);
        var loaded = repo.Get("job-1");

        Assert.NotNull(loaded);
        Assert.Equal("Hollow Knight", loaded!.GameName);
        Assert.Equal(JobState.Queued, loaded.State);
        Assert.Equal(job.CreatedAt, loaded.CreatedAt);
    }

    [Fact]
    public void Update_PersistsStateAndError()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        var job = new Job { Id = "job-2", GameName = "Celeste" };
        repo.Create(job);

        job.State = JobState.Failed;
        job.LastError = "boom";
        repo.Update(job);

        var loaded = repo.Get("job-2")!;
        Assert.Equal(JobState.Failed, loaded.State);
        Assert.Equal("boom", loaded.LastError);
    }

    [Fact]
    public void Update_InsertsWhenRowMissing()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        var job = new Job { Id = "ghost", GameName = "Ghost" };

        repo.Update(job);

        Assert.NotNull(repo.Get("ghost"));
    }

    [Fact]
    public void GetNextQueued_ReturnsOldestQueuedJob()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        var clock = new FakeClock();
        repo.Create(new Job { Id = "a", GameName = "A", State = JobState.Queued, CreatedAt = clock.UtcNow });
        repo.Create(new Job { Id = "b", GameName = "B", State = JobState.Queued, CreatedAt = clock.UtcNow.AddMinutes(1) });
        repo.Create(new Job { Id = "c", GameName = "C", State = JobState.Done, CreatedAt = clock.UtcNow.AddMinutes(-5) });

        Assert.Equal("a", repo.GetNextQueued()!.Id);
    }

    [Fact]
    public void GetInterrupted_ExcludesTerminalJobs()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        repo.Create(new Job { Id = "done", GameName = "D", State = JobState.Done });
        repo.Create(new Job { Id = "failed", GameName = "F", State = JobState.Failed });
        repo.Create(new Job { Id = "stuck", GameName = "S", State = JobState.Extracting });

        var interrupted = repo.GetInterrupted();

        Assert.Single(interrupted);
        Assert.Equal("stuck", interrupted[0].Id);
    }

    [Fact]
    public void Delete_RemovesJob()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        repo.Create(new Job { Id = "x", GameName = "X" });

        repo.Delete("x");

        Assert.Null(repo.Get("x"));
    }

    [Fact]
    public void Games_AddAndRead_RaisesEvent()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        Game? observed = null;
        repo.GameAdded += g => observed = g;

        repo.Add(new Game
        {
            Id = "g1",
            Title = "Hollow Knight",
            InstallPath = @"C:\Games\Hollow Knight",
            ExecutablePath = @"C:\Games\Hollow Knight\hk.exe",
            CoverUrl = "https://cdn.test/c.png",
        });

        Assert.NotNull(observed);
        Assert.Equal("Hollow Knight", observed!.Title);
        Assert.Single(repo.GetAllGames());
        Assert.Equal("g1", repo.GetGame("g1")!.Id);
    }

    [Fact]
    public void Games_UpdateAndDelete()
    {
        using var workspace = new TempWorkspace();
        var repo = Create(workspace);
        var game = new Game { Id = "g2", Title = "Old", InstallPath = "p", ExecutablePath = "e" };
        repo.Add(game);

        game.Title = "New";
        repo.Update(game);
        Assert.Equal("New", repo.GetGame("g2")!.Title);

        repo.DeleteGame("g2");
        Assert.Null(repo.GetGame("g2"));
    }
}
