using System.Text;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;

namespace SevenSeas.Tests;

/// <summary>Creates an isolated temp directory per test and removes it afterwards.</summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "sevenseas-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Resolve(string relative) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));

    public string CreateFile(string relative, byte[] bytes)
    {
        var path = Resolve(relative);
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string CreateFile(string relative, string content)
        => CreateFile(relative, Encoding.UTF8.GetBytes(content));

    public string CreateDirectory(string relative)
    {
        var path = Resolve(relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup.
        }
    }
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
}

public sealed class FakeSettingsService : ISettingsService
{
    public FakeSettingsService(AppSettings? settings = null)
    {
        Current = settings ?? AppSettings.CreateDefault();
    }

    public AppSettings Current { get; private set; }

    public string SettingsFilePath { get; } = "memory://settings.json";

    public event Action<AppSettings>? SettingsChanged;

    public void Load() => SettingsChanged?.Invoke(Current);

    public void Save() => SettingsChanged?.Invoke(Current);

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        SettingsChanged?.Invoke(Current);
    }
}

public sealed class FakeArchiveVerifier : IArchiveVerifier
{
    public ArchiveVerificationResult Result { get; set; } = ArchiveVerificationResult.Valid(ArchiveKind.Zip);

    public string? LastPath { get; private set; }

    public ArchiveVerificationResult Verify(string filePath)
    {
        LastPath = filePath;
        return Result;
    }
}

public sealed class FakeMetadataService : IMetadataService
{
    public GameMetadata Result { get; set; } = new("Hollow Knight", "https://cdn.test/cover.png");

    public Exception? ThrowOnLookup { get; set; }

    public string? LastQuery { get; private set; }

    public Task<GameMetadata> LookupAsync(string fileOrGameName, CancellationToken cancellationToken = default)
    {
        LastQuery = fileOrGameName;
        if (ThrowOnLookup is not null)
        {
            throw ThrowOnLookup;
        }

        return Task.FromResult(Result);
    }
}

public sealed class FakeArchiveExtractor : IArchiveExtractor
{
    /// <summary>Results returned in order; the last entry repeats once exhausted.</summary>
    public List<ExtractionResult> Results { get; } = new();

    public int CallCount { get; private set; }

    public List<string?> Passwords { get; } = new();

    public Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Passwords.Add(password);

        if (Results.Count == 0)
        {
            Directory.CreateDirectory(destinationFolder);
            return Task.FromResult(ExtractionResult.Ok(destinationFolder));
        }

        var index = Math.Min(CallCount - 1, Results.Count - 1);
        var result = Results[index];

        if (result.Success && result.DestinationFolder is not null)
        {
            Directory.CreateDirectory(result.DestinationFolder);
        }

        return Task.FromResult(result);
    }
}

public sealed class FakeExecutableFinder : IExecutableFinder
{
    public ExecutableSearchResult Result { get; set; } =
        ExecutableSearchResult.Chosen(@"C:\Games\Game\game.exe", Array.Empty<ExecutableCandidate>());

    public ExeCandidateAccessor CandidateFile { get; } = new();

    public ExecutableSearchResult Find(string installFolder)
    {
        CandidateFile.LastFolder = installFolder;
        return Result;
    }

    public IReadOnlyList<ExecutableCandidate> ListCandidates(string installFolder)
        => Result.Candidates;

    public sealed class ExeCandidateAccessor
    {
        public string? LastFolder { get; set; }
    }
}

public sealed class FakePasswordSource : IArchivePasswordSource
{
    public string? Hint { get; set; }

    public string? GetPasswordHint(string? sourceUrl) => Hint;
}

public sealed class ScriptedUserInteraction : IUserInteraction
{
    public Queue<string?> Passwords { get; } = new();

    public string? ExecutableChoice { get; set; }

    public bool ResumeAnswer { get; set; }

    public int PasswordPrompts { get; private set; }

    public int ExecutablePrompts { get; private set; }

    public int ResumePrompts { get; private set; }

    public Task<string?> RequestArchivePasswordAsync(string archivePath, string? passwordHint, CancellationToken cancellationToken = default)
    {
        PasswordPrompts++;
        return Task.FromResult(Passwords.Count > 0 ? Passwords.Dequeue() : null);
    }

    public Task<string?> RequestExecutableChoiceAsync(IReadOnlyList<ExecutableCandidate> candidates, CancellationToken cancellationToken = default)
    {
        ExecutablePrompts++;
        return Task.FromResult(ExecutableChoice);
    }

    public Task<bool> ConfirmResumeInterruptedJobsAsync(IReadOnlyList<Job> interruptedJobs, CancellationToken cancellationToken = default)
    {
        ResumePrompts++;
        return Task.FromResult(ResumeAnswer);
    }
}

/// <summary>Returns canned HTTP responses keyed by a URL substring.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string Match, Func<HttpRequestMessage, HttpResponseMessage> Factory)> _routes = new();

    public int CallCount { get; private set; }

    public StubHttpMessageHandler Route(string match, string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
        => Route(match, _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public StubHttpMessageHandler Route(string match, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _routes.Add((match, factory));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var url = request.RequestUri?.ToString() ?? string.Empty;
        foreach (var (match, factory) in _routes)
        {
            if (url.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(factory(request));
            }
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
