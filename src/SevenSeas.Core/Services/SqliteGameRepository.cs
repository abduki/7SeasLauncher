using Dapper;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// The SQLite-backed Job Queue and Library Repository.
/// One class implements both tables because they share a single connection and schema bootstrap.
/// </summary>
public sealed class SqliteGameRepository : IJobRepository, IGameRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteGameRepository> _logger;

    public SqliteGameRepository(string databasePath, ILogger<SqliteGameRepository>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteGameRepository>.Instance;
    }

    public string DatabasePath { get; }

    public event Action<Game>? GameAdded;

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Jobs (
              Id             TEXT PRIMARY KEY,
              GameName       TEXT NOT NULL,
              SourceUrl      TEXT,
              DownloadedPath TEXT,
              FinalPath      TEXT,
              ExecutablePath TEXT,
              State          TEXT NOT NULL,
              LastError      TEXT,
              CreatedAt      TEXT NOT NULL,
              UpdatedAt      TEXT NOT NULL
            );
            """);
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Games (
              Id             TEXT PRIMARY KEY,
              Title          TEXT NOT NULL,
              InstallPath    TEXT NOT NULL,
              ExecutablePath TEXT NOT NULL,
              CoverUrl       TEXT,
              AddedAt        TEXT NOT NULL
            );
            """);
        connection.Execute("CREATE INDEX IF NOT EXISTS IX_Jobs_State ON Jobs(State, CreatedAt);");

        _logger.LogInformation("Database initialised at {Path}.", DatabasePath);
    }

    // ---------- Jobs ----------

    public Job Create(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.UpdatedAt = job.CreatedAt;
        using var connection = OpenConnection();
        connection.Execute("""
            INSERT INTO Jobs (Id, GameName, SourceUrl, DownloadedPath, FinalPath, ExecutablePath, State, LastError, CreatedAt, UpdatedAt)
            VALUES (@Id, @GameName, @SourceUrl, @DownloadedPath, @FinalPath, @ExecutablePath, @State, @LastError, @CreatedAt, @UpdatedAt);
            """,
            new
            {
                job.Id,
                job.GameName,
                job.SourceUrl,
                job.DownloadedPath,
                job.FinalPath,
                job.ExecutablePath,
                State = job.State.ToString(),
                job.LastError,
                CreatedAt = Format(job.CreatedAt),
                UpdatedAt = Format(job.UpdatedAt),
            });
        _logger.LogInformation("Created job {JobId} ({GameName}) in state {State}.", job.Id, job.GameName, job.State);
        return job;
    }

    public void Update(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        using var connection = OpenConnection();
        var affected = connection.Execute("""
            UPDATE Jobs SET
              GameName = @GameName,
              SourceUrl = @SourceUrl,
              DownloadedPath = @DownloadedPath,
              FinalPath = @FinalPath,
              ExecutablePath = @ExecutablePath,
              State = @State,
              LastError = @LastError,
              UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """,
            new
            {
                job.Id,
                job.GameName,
                job.SourceUrl,
                job.DownloadedPath,
                job.FinalPath,
                job.ExecutablePath,
                State = job.State.ToString(),
                job.LastError,
                UpdatedAt = Format(job.UpdatedAt),
            });

        if (affected == 0)
        {
            // Never lose a row: fall back to insert if the row vanished (e.g. deleted concurrently).
            Create(job);
        }
    }

    public Job? Get(string id)
    {
        using var connection = OpenConnection();
        var row = connection.QuerySingleOrDefault<JobRow>(
            "SELECT * FROM Jobs WHERE Id = @id;", new { id });
        return row is null ? null : Map(row);
    }

    public IReadOnlyList<Job> GetAll()
    {
        using var connection = OpenConnection();
        return connection.Query<JobRow>("SELECT * FROM Jobs ORDER BY CreatedAt;").Select(Map).ToList();
    }

    public IReadOnlyList<Job> GetByState(JobState state)
    {
        using var connection = OpenConnection();
        return connection
            .Query<JobRow>("SELECT * FROM Jobs WHERE State = @state ORDER BY CreatedAt;", new { state = state.ToString() })
            .Select(Map)
            .ToList();
    }

    public Job? GetNextQueued()
    {
        using var connection = OpenConnection();
        var row = connection.QueryFirstOrDefault<JobRow>(
            "SELECT * FROM Jobs WHERE State = @state ORDER BY CreatedAt LIMIT 1;",
            new { state = JobState.Queued.ToString() });
        return row is null ? null : Map(row);
    }

    public IReadOnlyList<Job> GetInterrupted()
    {
        using var connection = OpenConnection();
        return connection
            .Query<JobRow>(
                "SELECT * FROM Jobs WHERE State NOT IN (@done, @failed) ORDER BY CreatedAt;",
                new { done = JobState.Done.ToString(), failed = JobState.Failed.ToString() })
            .Select(Map)
            .ToList();
    }

    public void Delete(string id)
    {
        using var connection = OpenConnection();
        connection.Execute("DELETE FROM Jobs WHERE Id = @id;", new { id });
    }

    public int ClearFinished()
    {
        using var connection = OpenConnection();
        var removed = connection.Execute(
            "DELETE FROM Jobs WHERE State IN (@done, @failed);",
            new { done = JobState.Done.ToString(), failed = JobState.Failed.ToString() });

        if (removed > 0)
        {
            _logger.LogInformation("Cleared {Count} finished job(s).", removed);
        }

        return removed;
    }

    // ---------- Games ----------

    public void Add(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        using (var connection = OpenConnection())
        {
            connection.Execute("""
                INSERT INTO Games (Id, Title, InstallPath, ExecutablePath, CoverUrl, AddedAt)
                VALUES (@Id, @Title, @InstallPath, @ExecutablePath, @CoverUrl, @AddedAt);
                """,
                new
                {
                    game.Id,
                    game.Title,
                    game.InstallPath,
                    game.ExecutablePath,
                    game.CoverUrl,
                    AddedAt = Format(game.AddedAt),
                });
        }

        _logger.LogInformation("Library Repo wrote game '{Title}' at {Path}.", game.Title, game.InstallPath);
        GameAdded?.Invoke(game);
    }

    public void Update(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        using var connection = OpenConnection();
        connection.Execute("""
            UPDATE Games SET
              Title = @Title,
              InstallPath = @InstallPath,
              ExecutablePath = @ExecutablePath,
              CoverUrl = @CoverUrl
            WHERE Id = @Id;
            """,
            new { game.Id, game.Title, game.InstallPath, game.ExecutablePath, game.CoverUrl });
    }

    public Game? GetGame(string id)
    {
        using var connection = OpenConnection();
        var row = connection.QuerySingleOrDefault<GameRow>(
            "SELECT * FROM Games WHERE Id = @id;", new { id });
        return row is null ? null : MapGame(row);
    }

    public IReadOnlyList<Game> GetAllGames()
    {
        using var connection = OpenConnection();
        return connection.Query<GameRow>("SELECT * FROM Games ORDER BY AddedAt;").Select(MapGame).ToList();
    }

    public void DeleteGame(string id)
    {
        using var connection = OpenConnection();
        connection.Execute("DELETE FROM Games WHERE Id = @id;", new { id });
    }

    // ---------- Internals ----------

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("o");

    private static DateTimeOffset Parse(string? value) =>
        DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static Job Map(JobRow row) => new()
    {
        Id = row.Id,
        GameName = row.GameName,
        SourceUrl = row.SourceUrl,
        DownloadedPath = row.DownloadedPath,
        FinalPath = row.FinalPath,
        ExecutablePath = row.ExecutablePath,
        State = Enum.TryParse<JobState>(row.State, ignoreCase: true, out var state) ? state : JobState.Failed,
        LastError = row.LastError,
        CreatedAt = Parse(row.CreatedAt),
        UpdatedAt = Parse(row.UpdatedAt),
    };

    private static Game MapGame(GameRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        InstallPath = row.InstallPath,
        ExecutablePath = row.ExecutablePath,
        CoverUrl = row.CoverUrl,
        AddedAt = Parse(row.AddedAt),
    };

    /// <summary>Flat row shape so Dapper never has to convert the State enum from TEXT.</summary>
    private sealed class JobRow
    {
        public string Id { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public string? DownloadedPath { get; set; }
        public string? FinalPath { get; set; }
        public string? ExecutablePath { get; set; }
        public string State { get; set; } = string.Empty;
        public string? LastError { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class GameRow
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string? CoverUrl { get; set; }
        public string AddedAt { get; set; } = string.Empty;
    }
}
