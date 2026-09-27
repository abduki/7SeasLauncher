namespace SevenSeas.Core.Abstractions;

/// <summary>
/// Supplies a site-level archive password hint.
/// Kept separate from <see cref="IUserInteraction"/> so the pipeline can try the
/// hint automatically before bothering the user.
/// </summary>
public interface IArchivePasswordSource
{
    /// <summary>Returns the hint for a source URL, or null when the scheme declares none.</summary>
    string? GetPasswordHint(string? sourceUrl);
}
