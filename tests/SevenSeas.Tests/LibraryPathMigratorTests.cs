using SevenSeas.Core.Models;
using SevenSeas.Core.Services;

namespace SevenSeas.Tests;

public sealed class LibraryPathMigratorTests
{
    private static Game Game_(string installPath, string executablePath = "", string title = "Slow Roads")
        => new() { Title = title, InstallPath = installPath, ExecutablePath = executablePath };

    /// <summary>Pretends a set of folders exists so the planner can be driven without touching disk.</summary>
    private static Func<string, bool> Existing(params string[] folders)
    {
        var set = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
        return path => set.Contains(path);
    }

    [Fact]
    public void Plan_MarksAGameThatIsStillInTheOldFolder()
    {
        var game = Game_(@"C:\Games\Slow Roads");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing(@"C:\Games\Slow Roads"));

        var relocation = Assert.Single(plan);
        Assert.Equal(GameRelocationState.StillAtOldRoot, relocation.State);
        Assert.Equal(@"D:\Games\Slow Roads", relocation.InstallPath);
    }

    [Fact]
    public void Plan_MarksAGameThatIsAlreadyInTheNewFolder()
    {
        var game = Game_(@"C:\Games\Slow Roads");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing(@"D:\Games\Slow Roads"));

        var relocation = Assert.Single(plan);
        Assert.Equal(GameRelocationState.AlreadyAtNewRoot, relocation.State);
    }

    [Fact]
    public void Plan_MarksAGameThatIsNowhere()
    {
        var game = Game_(@"C:\Games\Slow Roads");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing());

        var relocation = Assert.Single(plan);
        Assert.Equal(GameRelocationState.Missing, relocation.State);
    }

    [Fact]
    public void Plan_KeepsTheExecutableInTheSamePlaceInsideTheGame()
    {
        var game = Game_(@"C:\Games\Slow Roads", @"C:\Games\Slow Roads\bin\game.exe");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing());

        Assert.Equal(@"D:\Games\Slow Roads\bin\game.exe", Assert.Single(plan).ExecutablePath);
    }

    [Fact]
    public void Plan_GivesUpOnAnExecutableOutsideTheGameFolder()
    {
        var game = Game_(@"C:\Games\Slow Roads", @"C:\Tools\launcher.exe");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing());

        Assert.Null(Assert.Single(plan).ExecutablePath);
    }

    [Fact]
    public void Plan_IgnoresAGameSittingDirectlyInTheRoot()
    {
        var game = Game_(@"C:\Games");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing(@"C:\Games"));

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_DoesNothingWithoutANewRoot()
    {
        var game = Game_(@"C:\Games\Slow Roads");
        Assert.Empty(LibraryPathMigrator.Plan([game], @"C:\Games", "  ", Existing(@"C:\Games\Slow Roads")));
    }

    [Fact]
    public void Apply_WritesBothPathsOntoTheGame()
    {
        var game = Game_(@"C:\Games\Slow Roads", @"C:\Games\Slow Roads\game.exe");
        var plan = LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing());
        var relocation = Assert.Single(plan);

        LibraryPathMigrator.Apply(relocation);

        Assert.Equal(@"D:\Games\Slow Roads", game.InstallPath);
        Assert.Equal(@"D:\Games\Slow Roads\game.exe", game.ExecutablePath);
    }

    [Fact]
    public void Apply_LeavesTheExecutableAloneWhenItCouldNotBeMapped()
    {
        var game = Game_(@"C:\Games\Slow Roads", @"C:\Tools\launcher.exe");
        var relocation = Assert.Single(
            LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing()));

        LibraryPathMigrator.Apply(relocation);

        Assert.Equal(@"D:\Games\Slow Roads", game.InstallPath);
        Assert.Equal(@"C:\Tools\launcher.exe", game.ExecutablePath);
    }

    [Fact]
    public void TryMove_RefusesWhenSomethingIsAlreadyThere()
    {
        var game = Game_(@"C:\Games\Slow Roads");
        var relocation = Assert.Single(
            LibraryPathMigrator.Plan([game], @"C:\Games", @"D:\Games", Existing(@"D:\Games\Slow Roads")));

        // The destination check is real, so nothing is moved and the reason is reported.
        Assert.False(LibraryPathMigrator.TryMove(relocation, out var error, (_, _) => { }));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryMove_DoesNothingWhenTheLocationIsUnchanged()
    {
        var game = Game_(@"C:\Games\Slow Roads");
        var relocation = new GameRelocation(game, GameRelocationState.AlreadyAtNewRoot, game.InstallPath, null);

        Assert.True(LibraryPathMigrator.TryMove(relocation, out var error));
        Assert.Empty(error);
    }
}
