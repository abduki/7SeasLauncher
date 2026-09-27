using SevenSeas.Core.Models;

namespace SevenSeas.Core.Abstractions;

/// <summary>
/// The one place the headless Core is allowed to ask the UI for a decision.
/// Implemented by the WPF layer (Launchpad: password prompt, executable picker, resume dialog).
/// The default implementation used by tests answers "no" to everything.
/// </summary>
public interface IUserInteraction
{
    /// <summary>Prompts for an archive password. Returns null when the user cancels.</summary>
    Task<string?> RequestArchivePasswordAsync(
        string archivePath,
        string? passwordHint,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the user to pick between ambiguous executables. Returns the chosen path or null.</summary>
    Task<string?> RequestExecutableChoiceAsync(
        IReadOnlyList<ExecutableCandidate> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>Offers to resume interrupted jobs on launch. Returns true to resume.</summary>
    Task<bool> ConfirmResumeInterruptedJobsAsync(
        IReadOnlyList<Job> interruptedJobs,
        CancellationToken cancellationToken = default);
}

/// <summary>Answers every prompt with "cancel"/"no". Safe default for headless runs and tests.</summary>
public sealed class NullUserInteraction : IUserInteraction
{
    public Task<string?> RequestArchivePasswordAsync(string archivePath, string? passwordHint, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<string?> RequestExecutableChoiceAsync(IReadOnlyList<ExecutableCandidate> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<bool> ConfirmResumeInterruptedJobsAsync(IReadOnlyList<Job> interruptedJobs, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
