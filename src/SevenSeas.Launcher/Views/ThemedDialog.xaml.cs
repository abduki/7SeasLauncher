using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace SevenSeas.Launcher.Views;

/// <summary>
/// A dialog that matches the rest of the app. The stock Windows message box is light-themed and
/// looks nothing like 7SeasLauncher, so every confirmation and error goes through here instead.
/// </summary>
public partial class ThemedDialog : FluentWindow
{
    private ThemedDialog(string heading, string message, IReadOnlyList<(string Label, int Value, bool IsCancel)> buttons)
    {
        InitializeComponent();

        Title = heading;
        DialogTitleBar.Title = heading;
        HeadingText.Text = heading;
        MessageText.Text = message;

        foreach (var button in buttons)
        {
            var control = new System.Windows.Controls.Button
            {
                Content = button.Label,
                MinWidth = 110,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = button.IsCancel,
                IsDefault = button.Value == 0,
            };

            var value = button.Value;
            control.Click += (_, _) =>
            {
                Result = value;
                Close();
            };

            ButtonRow.Children.Add(control);
        }
    }

    /// <summary>Index of the button that was pressed; -1 when the window was dismissed.</summary>
    public int Result { get; private set; } = -1;

    public static int Show(
        Window? owner,
        string heading,
        string message,
        params (string Label, bool IsCancel)[] buttons)
    {
        var mapped = buttons
            .Select((button, index) => (button.Label, Value: index, button.IsCancel))
            .ToList();

        var dialog = new ThemedDialog(heading, message, mapped);

        if (owner is { IsLoaded: true })
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }
}

/// <summary>Convenience wrappers so call sites stay readable.</summary>
public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    /// <summary>Yes/No confirmation. True when the first button was chosen.</summary>
    public static bool Confirm(string heading, string message, string confirmLabel = "Continue", string cancelLabel = "Cancel")
        => ThemedDialog.Show(Owner, heading, message, (confirmLabel, false), (cancelLabel, true)) == 0;

    /// <summary>Two-way choice with a cancel. Returns 0 for the first button, 1 for the second, -1 for cancel.</summary>
    public static int Choose(string heading, string message, string firstLabel, string secondLabel, string cancelLabel = "Cancel")
        => ThemedDialog.Show(Owner, heading, message, (firstLabel, false), (secondLabel, false), (cancelLabel, true));

    /// <summary>Single-button notice.</summary>
    public static void Inform(string heading, string message)
        => ThemedDialog.Show(Owner, heading, message, ("OK", false));
}
