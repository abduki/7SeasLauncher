using System.Windows;
using SevenSeas.Core.Models;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace SevenSeas.Launcher.Views;

/// <summary>
/// Lets the user correct a game's title (and the executable it launches) after it has been installed.
/// </summary>
public partial class GameEditorDialog : FluentWindow
{
    public GameEditorDialog(string title, string executablePath, IReadOnlyList<ExecutableCandidate> candidates)
    {
        InitializeComponent();

        TitleBox.Text = title;

        // Every executable in the folder, most likely first, so a game with several can be repointed.
        foreach (var candidate in candidates)
        {
            ExeBox.Items.Add(candidate.Path);
        }

        ExeBox.Text = executablePath;
        HintText.Text = candidates.Count > 1
            ? $"{candidates.Count} executables found in the game folder. Pick the one that should launch."
            : "This is the executable 7SeasLauncher launches.";
    }

    /// <summary>The edited title, valid after a true dialog result.</summary>
    public string GameTitle { get; private set; } = string.Empty;

    public string ExecutablePath { get; private set; } = string.Empty;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the game's executable",
            Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        var currentDirectory = System.IO.Path.GetDirectoryName(ExeBox.Text);
        if (!string.IsNullOrWhiteSpace(currentDirectory) && System.IO.Directory.Exists(currentDirectory))
        {
            dialog.InitialDirectory = currentDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            ExeBox.Text = dialog.FileName;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        var executable = ExeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            HintText.Text = "Give the game a title first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(executable))
        {
            HintText.Text = "Choose the executable that launches the game.";
            return;
        }

        GameTitle = title;
        ExecutablePath = executable;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
