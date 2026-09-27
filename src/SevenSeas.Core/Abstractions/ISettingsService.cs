using SevenSeas.Core.Models;

namespace SevenSeas.Core.Abstractions;

/// <summary>
/// The single source of truth for configuration.
/// Nothing hardcodes a path; anything that needs a folder asks Settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>The live settings instance. Callers must not mutate it directly; use <see cref="Update"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Absolute path to settings.json.</summary>
    string SettingsFilePath { get; }

    /// <summary>Loads settings from disk, creating defaults when the file is missing or corrupt.</summary>
    void Load();

    /// <summary>Persists the current settings atomically.</summary>
    void Save();

    /// <summary>Applies a mutation and persists it, raising <see cref="SettingsChanged"/>.</summary>
    void Update(Action<AppSettings> mutate);

    /// <summary>Raised after settings are loaded or updated.</summary>
    event Action<AppSettings>? SettingsChanged;
}
