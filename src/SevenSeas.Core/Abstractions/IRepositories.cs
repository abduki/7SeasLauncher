using SevenSeas.Core.Models;

namespace SevenSeas.Core.Abstractions;

/// <summary>The persistent Jobs table. SQLite is the source of truth.</summary>
public interface IJobRepository
{
    /// <summary>Creates the schema if it does not exist. Safe to call repeatedly (idempotent).</summary>
    void Initialize();

    Job Create(Job job);

    void Update(Job job);

    Job? Get(string id);

    IReadOnlyList<Job> GetAll();

    IReadOnlyList<Job> GetByState(JobState state);

    /// <summary>The oldest queued job, or null when the queue is empty. FIFO ordering.</summary>
    Job? GetNextQueued();

    /// <summary>Jobs left in a non-terminal state, e.g. after a crash.</summary>
    IReadOnlyList<Job> GetInterrupted();

    void Delete(string id);

    /// <summary>Removes every finished job (Done or Failed) and returns how many rows went.</summary>
    int ClearFinished();
}

/// <summary>The Games table, with change notifications so the Library view can refresh.</summary>
public interface IGameRepository
{
    void Initialize();

    void Add(Game game);

    void Update(Game game);

    Game? GetGame(string id);

    IReadOnlyList<Game> GetAllGames();

    void DeleteGame(string id);

    /// <summary>Raised after a game row is inserted, so the Library View can refresh its grid.</summary>
    event Action<Game>? GameAdded;
}
