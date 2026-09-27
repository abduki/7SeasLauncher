using System.Windows;
using SevenSeas.Core.Models;
using SevenSeas.Launcher.Services;
using Wpf.Ui.Controls;

namespace SevenSeas.Launcher.Views;

/// <summary>
/// Explains that the antivirus probably interfered and shows exactly which folder to exclude.
/// </summary>
public partial class AntivirusExclusionDialog : FluentWindow
{
    private readonly AntivirusExclusionService _exclusions;

    public AntivirusExclusionDialog(AntivirusExclusionService exclusions, DownloadProblemAdvice? advice = null)
    {
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));

        InitializeComponent();

        HeadingText.Text = advice?.Title ?? "Antivirus exclusions";
        ExplanationText.Text = advice?.Explanation ??
            "Games are downloaded into a temp folder and then unpacked. Some antivirus products quarantine " +
            "archives while that is happening, which makes a finished download look like it disappeared. " +
            "Excluding 7SeasLauncher's folder prevents that.";

        DialogTitleBar.Title = "Antivirus exclusions";

        var folders = _exclusions.RecommendedFolders();
        FoldersBox.Text = folders.Count > 0
            ? string.Join(Environment.NewLine, folders)
            : "(7SeasLauncher's folders are not configured yet)";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(FoldersBox.Text);
            StatusText.Text = "Path copied. Paste it into the exclusions list.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not copy the path: " + ex.Message;
        }
    }

    private void OnOpenSecurityClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = _exclusions.OpenWindowsSecurity()
            ? "Windows Security is opening. Add the folder above under Exclusions."
            : "Could not open Windows Security. Open it from the Start menu instead.";
    }

    private void OnAddAutomaticallyClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = _exclusions.TryAddExclusions(out var message)
            ? message
            : message;
    }

    /// <summary>True when the user asked not to be reminded again.</summary>
    public bool DoNotShowAgain => DoNotShowAgainBox.IsChecked == true;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
