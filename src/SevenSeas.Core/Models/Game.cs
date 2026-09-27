namespace SevenSeas.Core.Models;

/// <summary>
/// A completed, installed game. Written by the Library Repo
/// when a pipeline job reaches <see cref="JobState.Done"/>.
/// </summary>
public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string InstallPath { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Remote cover-art URL from SteamGridDB, or null when lookup fell back to a placeholder.</summary>
    public string? CoverUrl { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
