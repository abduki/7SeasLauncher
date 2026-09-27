namespace SevenSeas.Launcher.Services;

/// <summary>
/// Single place for transient user-facing messages. Views and view models report here instead of
/// drawing their own inline status text, so feedback always appears in the shell status bar.
/// </summary>
public sealed class StatusService
{
    public event Action<string>? Reported;

    public void Report(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Reported?.Invoke(message);
        }
    }
}
