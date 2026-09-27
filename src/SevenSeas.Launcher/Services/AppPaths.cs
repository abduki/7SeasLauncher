namespace SevenSeas.Launcher.Services;

/// <summary>Well-known on-disk locations.</summary>
public static class AppPaths
{
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7SeasLauncher");

    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    public static string DatabaseFile => Path.Combine(AppDataRoot, "7seas.db");

    public static string LogsFolder => Path.Combine(AppDataRoot, "logs");

    /// <summary>Schemes ship next to the executable so users can drop JSON files beside the app.</summary>
    public static string SchemesFolder => Path.Combine(AppContext.BaseDirectory, "schemes");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LogsFolder);
    }
}
