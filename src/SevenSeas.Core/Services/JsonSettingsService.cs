using System.Text.Json;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Core.Services;

/// <summary>
/// Loads and saves settings.json.
/// Writes are atomic (temp file + move) so a crash mid-save can never corrupt configuration.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly object _gate = new();

    public JsonSettingsService(string settingsFilePath, ILogger<JsonSettingsService>? logger = null)
    {
        SettingsFilePath = settingsFilePath;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonSettingsService>.Instance;
        Current = AppSettings.CreateDefault();
    }

    public string SettingsFilePath { get; }

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? SettingsChanged;

    public void Load()
    {
        lock (_gate)
        {
            AppSettings loaded;
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _logger.LogInformation("settings.json not found at {Path}; creating defaults.", SettingsFilePath);
                    loaded = AppSettings.CreateDefault();
                    Current = loaded;
                    SaveCore(loaded);
                }
                else
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                             ?? AppSettings.CreateDefault();
                    loaded.WindowState ??= new WindowStateSettings();
                    loaded.HomePages ??= new List<SevenSeas.Core.Models.HomePage>();

                    Current = loaded;
                    _logger.LogInformation("Loaded settings from {Path}.", SettingsFilePath);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "settings.json was unreadable; falling back to defaults.");
                QuarantineCorruptFile();
                loaded = AppSettings.CreateDefault();
                Current = loaded;
            }

            EnsureDirectories(loaded);
        }

        SettingsChanged?.Invoke(Current);
    }

    public void Save()
    {
        lock (_gate)
        {
            SaveCore(Current);
        }

        SettingsChanged?.Invoke(Current);
    }

    public void Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        lock (_gate)
        {
            mutate(Current);
            EnsureDirectories(Current);
            SaveCore(Current);
        }

        SettingsChanged?.Invoke(Current);
    }

    private void SaveCore(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            var temp = SettingsFilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, SettingsFilePath, overwrite: true);
            _logger.LogDebug("Saved settings to {Path}.", SettingsFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}.", SettingsFilePath);
        }
    }

    private void EnsureDirectories(AppSettings settings)
    {
        foreach (var folder in new[] { settings.GamesFolder, settings.TempFolder, settings.TrashFolder })
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(folder);
                Directory.CreateDirectory(Path.Combine(folder, "downloads"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not create configured folder {Folder}.", folder);
            }
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Move(SettingsFilePath, SettingsFilePath + ".corrupt", overwrite: true);
            }
        }
        catch (IOException)
        {
            // Best effort only.
        }
    }
}
