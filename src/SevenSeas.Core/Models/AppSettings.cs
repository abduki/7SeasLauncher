namespace SevenSeas.Core.Models;

/// <summary>
/// The contents of settings.json. The Settings Service is the
/// single source of truth for every path, key and toggle in the app.
/// </summary>
public sealed class AppSettings
{
    public string GamesFolder { get; set; } = string.Empty;

    public string TempFolder { get; set; } = string.Empty;

    public string TrashFolder { get; set; } = string.Empty;

    public string SteamGridDbApiKey { get; set; } = string.Empty;

    public string Theme { get; set; } = "Dark";

    public WindowStateSettings WindowState { get; set; } = new();

    /// <summary>Last selected scheme, so the Browser can reopen where the user left off.</summary>
    public string? LastSchemeName { get; set; }

    /// <summary>When true the app offers to resume interrupted jobs on launch.</summary>
    public bool ResumeInterruptedJobs { get; set; } = true;

    /// <summary>Days a failed/completed archive is retained in Trash before cleanup.</summary>
    public int TrashRetentionDays { get; set; } = 7;

    /// <summary>
    /// Saved start pages (bookmarks). Opening one gives the user somewhere to browse from,
    /// which is how new site schemes get discovered.
    /// </summary>
    public List<HomePage> HomePages { get; set; } = new();

    /// <summary>The URL the Browse tab opens when it has no better idea.</summary>
    public string? DefaultHomePageUrl { get; set; }

    /// <summary>Set when the user ticks "don't show this again" on the antivirus reminder.</summary>
    public bool SuppressAntivirusReminder { get; set; }

    public static AppSettings CreateDefault(string? homeDirectory = null)
    {
        var home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var root = Path.Combine(home, "7SeasLauncher");

        return new AppSettings
        {
            GamesFolder = Path.Combine(root, "Games"),
            TempFolder = Path.Combine(root, "Temp"),
            TrashFolder = Path.Combine(root, "Trash"),
            SteamGridDbApiKey = string.Empty,
            Theme = "Dark",
            WindowState = new WindowStateSettings(),
        };
    }

    public AppSettings Clone() => new()
    {
        GamesFolder = GamesFolder,
        TempFolder = TempFolder,
        TrashFolder = TrashFolder,
        SteamGridDbApiKey = SteamGridDbApiKey,
        Theme = Theme,
        WindowState = WindowState.Clone(),
        LastSchemeName = LastSchemeName,
        ResumeInterruptedJobs = ResumeInterruptedJobs,
        TrashRetentionDays = TrashRetentionDays,
        HomePages = HomePages.Select(h => h.Clone()).ToList(),
        DefaultHomePageUrl = DefaultHomePageUrl,
        SuppressAntivirusReminder = SuppressAntivirusReminder,
    };
}

public sealed class WindowStateSettings
{
    public double Width { get; set; } = 1280;

    public double Height { get; set; } = 800;

    public double Left { get; set; } = 100;

    public double Top { get; set; } = 100;

    public bool IsMaximized { get; set; }

    public WindowStateSettings Clone() => new()
    {
        Width = Width,
        Height = Height,
        Left = Left,
        Top = Top,
        IsMaximized = IsMaximized,
    };
}
