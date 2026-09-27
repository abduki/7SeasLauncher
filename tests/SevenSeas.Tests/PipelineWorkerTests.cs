using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class PipelineWorkerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly SqliteGameRepository _repo;
    private readonly FakeSettingsService _settings = new();
    private readonly FakeArchiveVerifier _verifier = new();
    private readonly FakeMetadataService _metadata = new();
    private readonly FakeArchiveExtractor _extractor = new();
    private readonly FakeExecutableFinder _finder = new();
    private readonly FakePasswordSource _passwordSource = new();
    private readonly ScriptedUserInteraction _user = new();
    private readonly TrashService _trash;
    private readonly PipelineWorker _worker;
    private readonly string _archive;
    private readonly List<JobState> _observedStates = new();

    public PipelineWorkerTests()
    {
        _repo = new SqliteGameRepository(_workspace.Resolve("7seas.db"));
        _repo.Initialize();

        var games = _workspace.CreateDirectory("Games");
        var temp = _workspace.CreateDirectory("Temp");
        var trash = _workspace.CreateDirectory("Trash");
        _settings.Update(s =>
        {
            s.GamesFolder = games;
            s.TempFolder = temp;
            s.TrashFolder = trash;
        });

        _trash = new TrashService(_settings);
        _worker = new PipelineWorker(
            _repo, _repo, _settings, _verifier, _metadata, _extractor,
            _finder, _passwordSource, _user, _trash, new FakeClock());

        _worker.JobChanged += job => _observedStates.Add(job.State);
        _archive = _workspace.CreateFile("Temp/downloads/job1.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 });
    }

    public void Dispose() => _workspace.Dispose();

    private Job EnqueueJob(string gameName = "Hollow Knight")
    {
        var job = new Job
        {
            Id = Guid.NewGuid().ToString("N"),
            GameName = gameName,
            DownloadedPath = _archive,
            State = JobState.Queued,
        };
        _repo.Create(job);
        return job;
    }

    [Fact]
    public async Task HappyPath_WalksEveryStateAndInstallsGame()
    {
        var job = EnqueueJob();
        _metadata.Result = new GameMetadata("Hollow Knight", "https://cdn.test/hk.png", Matched: true);
        _finder.Result = ExecutableSearchResult.Chosen(
            _workspace.Resolve("Games/Hollow Knight/hk.exe"), Array.Empty<ExecutableCandidate>());

        var processed = await _worker.ProcessAllQueuedAsync();

        Assert.Equal(1, processed);
        var stored = _repo.Get(job.Id)!;
        Assert.Equal(JobState.Done, stored.State);
        Assert.Equal("Hollow Knight", stored.GameName);
        Assert.NotNull(stored.ExecutablePath);

        var games = _repo.GetAllGames();
        Assert.Single(games);
        Assert.Equal("Hollow Knight", games[0].Title);
        Assert.Equal("https://cdn.test/hk.png", games[0].CoverUrl);

        Assert.False(File.Exists(_archive));
        Assert.True(File.Exists(Path.Combine(_settings.Current.TrashFolder, job.Id + ".zip")));

        Assert.Contains(JobState.Verifying, _observedStates);
        Assert.Contains(JobState.FetchingMetadata, _observedStates);
        Assert.Contains(JobState.Extracting, _observedStates);
        Assert.Contains(JobState.FindingExecutable, _observedStates);
        Assert.Contains(JobState.Done, _observedStates);
    }

    [Fact]
    public async Task InvalidArchive_FailsAndTrashesFile()
    {
        var job = EnqueueJob();
        _verifier.Result = ArchiveVerificationResult.Invalid("Not a valid archive.");

        await _worker.ProcessAllQueuedAsync();

        var stored = _repo.Get(job.Id)!;
        Assert.Equal(JobState.Failed, stored.State);
        Assert.Equal("Not a valid archive.", stored.LastError);
        Assert.Empty(_repo.GetAllGames());
        Assert.True(File.Exists(Path.Combine(_settings.Current.TrashFolder, job.Id + ".zip")));
    }

    [Fact]
    public async Task ExtractionFailure_FailsJob()
    {
        var job = EnqueueJob();
        _extractor.Results.Add(ExtractionResult.Failed("disk full"));

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal(JobState.Failed, _repo.Get(job.Id)!.State);
        Assert.Contains("disk full", _repo.Get(job.Id)!.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordRequired_UserSuppliesPassword_ThenSucceeds()
    {
        var job = EnqueueJob();
        _extractor.Results.Add(ExtractionResult.NeedsPassword());
        _extractor.Results.Add(ExtractionResult.Ok(_workspace.Resolve("Games/Game")));
        _user.Passwords.Enqueue("letmein");
        _finder.Result = ExecutableSearchResult.Chosen(
            _workspace.Resolve("Games/Game/game.exe"), Array.Empty<ExecutableCandidate>());

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal(JobState.Done, _repo.Get(job.Id)!.State);
        Assert.Equal(1, _user.PasswordPrompts);
        Assert.Null(_extractor.Passwords[0]);
        Assert.Equal("letmein", _extractor.Passwords[1]);
    }

    [Fact]
    public async Task PasswordRequired_SchemeHintUsedFirst()
    {
        var job = EnqueueJob();
        _passwordSource.Hint = "site-password";
        _extractor.Results.Add(ExtractionResult.NeedsPassword());
        _extractor.Results.Add(ExtractionResult.Ok(_workspace.Resolve("Games/Game")));
        _user.Passwords.Enqueue("typed-by-user");
        _finder.Result = ExecutableSearchResult.Chosen(
            _workspace.Resolve("Games/Game/game.exe"), Array.Empty<ExecutableCandidate>());

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal("site-password", _extractor.Passwords[0]);
        Assert.Equal("typed-by-user", _extractor.Passwords[1]);
        Assert.Equal(JobState.Done, _repo.Get(job.Id)!.State);
    }

    [Fact]
    public async Task PasswordRequired_UserCancels_FailsJob()
    {
        var job = EnqueueJob();
        _extractor.Results.Add(ExtractionResult.NeedsPassword());

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal(JobState.Failed, _repo.Get(job.Id)!.State);
        Assert.Contains("password", _repo.Get(job.Id)!.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoExecutable_FailsJob()
    {
        var job = EnqueueJob();
        _finder.Result = ExecutableSearchResult.None(Array.Empty<ExecutableCandidate>());

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal(JobState.Failed, _repo.Get(job.Id)!.State);
        Assert.Contains("No executable found", _repo.Get(job.Id)!.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AmbiguousExecutable_UserPicksOne_Succeeds()
    {
        var job = EnqueueJob();
        var chosen = _workspace.CreateFile("Games/Game/chosen.exe", "MZ");
        _finder.Result = ExecutableSearchResult.Ambiguous(new[]
        {
            new ExecutableCandidate(chosen, 30, 1000),
            new ExecutableCandidate(_workspace.Resolve("Games/Game/other.exe"), 29, 900),
        });
        _user.ExecutableChoice = chosen;

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal(JobState.Done, _repo.Get(job.Id)!.State);
        Assert.Equal(chosen, _repo.Get(job.Id)!.ExecutablePath);
    }

    [Fact]
    public async Task AmbiguousExecutable_UserCancels_FailsJob()
    {
        var job = EnqueueJob();
        _finder.Result = ExecutableSearchResult.Ambiguous(new[]
        {
            new ExecutableCandidate(_workspace.Resolve("Games/Game/a.exe"), 30, 1000),
        });
        _user.ExecutableChoice = null;

        await _worker.ProcessAllQueuedAsync();

        Assert.Equal(JobState.Failed, _repo.Get(job.Id)!.State);
    }

    [Fact]
    public async Task MetadataFallback_UsesCleanedFilename()
    {
        var job = EnqueueJob("Hollow.Knight.v1.5.zip");
        _metadata.Result = GameMetadata.Fallback("Hollow Knight");
        _extractor.Results.Add(ExtractionResult.Ok(_workspace.Resolve("Games/Hollow Knight")));
        _finder.Result = ExecutableSearchResult.Chosen(
            _workspace.Resolve("Games/Hollow Knight/Hollow Knight.exe"), Array.Empty<ExecutableCandidate>());

        await _worker.ProcessAllQueuedAsync();

        var game = Assert.Single(_repo.GetAllGames());
        Assert.Equal("Hollow Knight", game.Title);
        Assert.Null(game.CoverUrl);
    }

    [Fact]
    public async Task ProcessNext_ReturnsFalseWhenQueueEmpty()
    {
        Assert.False(await _worker.ProcessNextAsync());
    }

    [Fact]
    public void NotifyJobAvailable_DoesNotThrowWhenAlreadySignalled()
    {
        _worker.NotifyJobAvailable();
        _worker.NotifyJobAvailable();
    }

    [Fact]
    public async Task ResumeInterrupted_RequeuesWhenConfirmed()
    {
        var job = EnqueueJob();
        _repo.Update(new Job
        {
            Id = job.Id,
            GameName = job.GameName,
            DownloadedPath = job.DownloadedPath,
            State = JobState.Extracting,
        });
        _user.ResumeAnswer = true;

        var count = await _worker.ResumeInterruptedAsync();

        Assert.Equal(1, count);
        Assert.Equal(JobState.Queued, _repo.Get(job.Id)!.State);
    }

    [Fact]
    public async Task ResumeInterrupted_DeclinedMarksFailed()
    {
        var job = EnqueueJob();
        _repo.Update(new Job
        {
            Id = job.Id,
            GameName = job.GameName,
            DownloadedPath = job.DownloadedPath,
            State = JobState.Extracting,
        });
        _user.ResumeAnswer = false;

        await _worker.ResumeInterruptedAsync();

        Assert.Equal(JobState.Failed, _repo.Get(job.Id)!.State);
    }

    [Fact]
    public async Task ResumeInterrupted_DownloadingWithoutFileFails()
    {
        var job = new Job { Id = "dl", GameName = "Game", State = JobState.Downloading };
        _repo.Create(job);
        _user.ResumeAnswer = true;

        await _worker.ResumeInterruptedAsync();

        Assert.Equal(JobState.Failed, _repo.Get("dl")!.State);
    }

    [Fact]
    public async Task ResumeInterrupted_NoJobs_ReturnsZero()
    {
        Assert.Equal(0, await _worker.ResumeInterruptedAsync());
    }

    [Fact]
    public async Task StartAndStop_RunsLoopWithoutError()
    {
        await _worker.StartAsync();
        _worker.NotifyJobAvailable();
        await Task.Delay(100);
        await _worker.StopAsync();
    }
}
