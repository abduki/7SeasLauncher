using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class LibraryFolderMigratorTests
{
    private static (LibraryFolderMigrator Migrator, SqliteGameRepository Repository) Create(
        TempWorkspace workspace,
        FakeExecutableFinder? finder = null)
    {
        var repository = new SqliteGameRepository(workspace.Resolve("7seas.db"));
        repository.Initialize();
        return (new LibraryFolderMigrator(repository, finder ?? new FakeExecutableFinder()), repository);
    }

    [Fact]
    public void CountStranded_CountsOnlyTheGamesStillInTheOldFolder()
    {
        var game = new Game { Title = "Slow Roads", InstallPath = @"C:\Games\Slow Roads" };
        var plan = LibraryPathMigrator.Plan(
            [game], @"C:\Games", @"D:\Games", path => path.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, LibraryFolderMigrator.CountStranded(plan));
    }

    [Fact]
    public void Commit_DoesNothingAtAllWithoutAPlan()
    {
        using var workspace = new TempWorkspace();
        var (migrator, _) = Create(workspace);

        Assert.Empty(migrator.Commit(Array.Empty<GameRelocation>(), moveThem: true));
    }

    [Fact]
    public void Commit_LeavesEverythingAloneWhenTheUserDeclinesTheMove()
    {
        using var workspace = new TempWorkspace();
        var (migrator, repository) = Create(workspace);

        var oldRoot = workspace.CreateDirectory("Games");
        var gameFolder = workspace.CreateDirectory("Games/Slow Roads");
        var game = new Game
        {
            Title = "Slow Roads",
            InstallPath = gameFolder,
            ExecutablePath = Path.Combine(gameFolder, "game.exe"),
        };
        repository.Add(game);

        var plan = migrator.Plan(oldRoot, workspace.Resolve("New Games"));
        var summary = migrator.Commit(plan, moveThem: false);

        Assert.Contains("left in the old folder", summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(gameFolder));
        Assert.Equal(gameFolder, repository.GetGame(game.Id)!.InstallPath);
    }

    [Fact]
    public void Commit_MovesTheFolderAndRepointsTheGame()
    {
        using var workspace = new TempWorkspace();
        var (migrator, repository) = Create(workspace);

        var oldRoot = workspace.CreateDirectory("Games");
        var gameFolder = workspace.CreateDirectory("Games/Slow Roads");
        var executable = workspace.CreateFile("Games/Slow Roads/game.exe", "MZ");
        var game = new Game
        {
            Title = "Slow Roads",
            InstallPath = gameFolder,
            ExecutablePath = executable,
        };
        repository.Add(game);

        var newRoot = workspace.Resolve("New Games");
        var summary = migrator.Commit(migrator.Plan(oldRoot, newRoot), moveThem: true);

        var expected = Path.Combine(newRoot, "Slow Roads");
        Assert.True(Directory.Exists(expected));
        Assert.False(Directory.Exists(gameFolder));
        Assert.Contains("moved", summary, StringComparison.OrdinalIgnoreCase);

        var stored = repository.GetGame(game.Id)!;
        Assert.Equal(expected, stored.InstallPath);
        Assert.Equal(Path.Combine(expected, "game.exe"), stored.ExecutablePath);
        Assert.True(File.Exists(stored.ExecutablePath));
    }

    [Fact]
    public void Commit_RepointsAGameTheUserAlreadyMovedThemselves()
    {
        using var workspace = new TempWorkspace();
        var (migrator, repository) = Create(workspace);

        // The row still points at the old root, but the files are already under the new one.
        var oldRoot = workspace.Resolve("Old Games");
        var newRoot = workspace.CreateDirectory("New Games");
        var newFolder = workspace.CreateDirectory("New Games/Slow Roads");
        var executable = workspace.CreateFile("New Games/Slow Roads/game.exe", "MZ");

        var game = new Game
        {
            Title = "Slow Roads",
            InstallPath = Path.Combine(oldRoot, "Slow Roads"),
            ExecutablePath = Path.Combine(oldRoot, "Slow Roads", "game.exe"),
        };
        repository.Add(game);

        var plan = migrator.Plan(oldRoot, newRoot);
        Assert.Equal(GameRelocationState.AlreadyAtNewRoot, Assert.Single(plan).State);

        migrator.Commit(plan, moveThem: false);

        var stored = repository.GetGame(game.Id)!;
        Assert.Equal(newFolder, stored.InstallPath);
        Assert.Equal(executable, stored.ExecutablePath);
    }

    [Fact]
    public void Commit_RePicksAnExecutableWhenTheRecordedOneIsGone()
    {
        using var workspace = new TempWorkspace();
        var finder = new FakeExecutableFinder();
        var (migrator, repository) = Create(workspace, finder);

        var oldRoot = workspace.CreateDirectory("Games");
        var gameFolder = workspace.CreateDirectory("Games/Slow Roads");
        workspace.CreateFile("Games/Slow Roads/found.exe", "MZ");

        var newRoot = workspace.Resolve("New Games");
        finder.Result = ExecutableSearchResult.Chosen(
            Path.Combine(newRoot, "Slow Roads", "found.exe"), Array.Empty<ExecutableCandidate>());

        var game = new Game
        {
            Title = "Slow Roads",
            InstallPath = gameFolder,
            ExecutablePath = Path.Combine(gameFolder, "missing.exe"),
        };
        repository.Add(game);

        migrator.Commit(migrator.Plan(oldRoot, newRoot), moveThem: true);

        var stored = repository.GetGame(game.Id)!;
        Assert.Equal(Path.Combine(newRoot, "Slow Roads", "found.exe"), stored.ExecutablePath);
        Assert.True(File.Exists(stored.ExecutablePath));
    }

    [Fact]
    public void Commit_ClearsTheExecutableWhenNothingCanBeFound()
    {
        using var workspace = new TempWorkspace();
        var finder = new FakeExecutableFinder { Result = ExecutableSearchResult.None(Array.Empty<ExecutableCandidate>()) };
        var (migrator, repository) = Create(workspace, finder);

        var oldRoot = workspace.CreateDirectory("Games");
        var gameFolder = workspace.CreateDirectory("Games/Slow Roads");
        var game = new Game
        {
            Title = "Slow Roads",
            InstallPath = gameFolder,
            ExecutablePath = Path.Combine(gameFolder, "missing.exe"),
        };
        repository.Add(game);

        migrator.Commit(migrator.Plan(oldRoot, workspace.Resolve("New Games")), moveThem: true);

        // Better an empty path, which the library reports, than a stale one that fails mutely.
        Assert.Empty(repository.GetGame(game.Id)!.ExecutablePath);
    }

    [Fact]
    public void Commit_ReportsGamesItCannotFind()
    {
        using var workspace = new TempWorkspace();
        var (migrator, repository) = Create(workspace);

        var game = new Game
        {
            Title = "Gone",
            InstallPath = workspace.Resolve("Games/Gone"),
            ExecutablePath = string.Empty,
        };
        repository.Add(game);

        var summary = migrator.Commit(
            migrator.Plan(workspace.Resolve("Games"), workspace.Resolve("New Games")), moveThem: true);

        Assert.Contains("missing", summary, StringComparison.OrdinalIgnoreCase);
    }
}
