using System.Windows;
using System.Windows.Controls;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using Microsoft.Extensions.Logging;

namespace SevenSeas.Launcher.Services;

/// <summary>
/// The WPF implementation of Core's <see cref="IUserInteraction"/>: password prompt,
/// manual executable picker and the resume-interrupted-jobs dialog.
/// </summary>
public sealed class WpfUserInteraction : IUserInteraction
{
    private readonly ILogger<WpfUserInteraction> _logger;

    public WpfUserInteraction(ILogger<WpfUserInteraction>? logger = null)
        => _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WpfUserInteraction>.Instance;

    public Task<string?> RequestArchivePasswordAsync(
        string archivePath,
        string? passwordHint,
        CancellationToken cancellationToken = default)
    {
        var result = OnUi(() =>
        {
            var window = CreateWindow("Archive password", 460);
            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = $"'{Path.GetFileName(archivePath)}' is password protected.",
                TextWrapping = TextWrapping.Wrap,
            });

            if (!string.IsNullOrWhiteSpace(passwordHint))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"Hint from the site scheme: {passwordHint}",
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }

            var input = new PasswordBox { Margin = new Thickness(0, 14, 0, 0) };
            panel.Children.Add(input);
            panel.Children.Add(BuildButtons(window, "Unlock"));

            window.Content = panel;
            window.Loaded += (_, _) => input.Focus();

            return window.ShowDialog() == true ? input.Password : null;
        });

        return Task.FromResult(result);
    }

    public Task<string?> RequestExecutableChoiceAsync(
        IReadOnlyList<ExecutableCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var result = OnUi(() =>
        {
            var window = CreateWindow("Choose the game executable", 620);
            var panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock
            {
                Text = "More than one executable could launch this game. Pick the one that starts it:",
                TextWrapping = TextWrapping.Wrap,
            });

            var list = new ListBox { Margin = new Thickness(0, 14, 0, 0), MaxHeight = 360 };
            foreach (var candidate in candidates.OrderByDescending(c => c.Score))
            {
                list.Items.Add(new ListBoxItem
                {
                    Content = Path.GetFileName(candidate.Path),
                    Tag = candidate.Path,
                    ToolTip = candidate.Path,
                });
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }

            panel.Children.Add(list);
            panel.Children.Add(BuildButtons(window, "Use this executable"));

            window.Content = panel;

            var confirmed = window.ShowDialog() == true;
            return confirmed && list.SelectedItem is ListBoxItem item ? item.Tag as string : null;
        });

        return Task.FromResult(result);
    }

    public Task<bool> ConfirmResumeInterruptedJobsAsync(
        IReadOnlyList<Job> interruptedJobs,
        CancellationToken cancellationToken = default)
    {
        var result = OnUi(() =>
        {
            var preview = string.Join(Environment.NewLine, interruptedJobs.Take(8).Select(j => "  • " + j.GameName));
            if (interruptedJobs.Count > 8)
            {
                preview += Environment.NewLine + $"  … and {interruptedJobs.Count - 8} more";
            }

            var message = $"7SeasLauncher found {interruptedJobs.Count} unfinished job(s) from a previous session:" +
                          Environment.NewLine + Environment.NewLine + preview +
                          Environment.NewLine + Environment.NewLine + "Resume them now?";

            return Views.Dialogs.Confirm("Resume interrupted jobs?", message, "Resume", "Skip");
        });

        return Task.FromResult(result);
    }

    private static StackPanel BuildButtons(Window window, string confirmText)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var confirm = new Button { Content = confirmText, MinWidth = 130, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        confirm.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) =>
        {
            window.DialogResult = false;
            window.Close();
        };

        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        return buttons;
    }

    private static Window CreateWindow(string title, double width) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        ResizeMode = ResizeMode.NoResize,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = GetOwner(),
    };

    private static Window? GetOwner()
    {
        var main = Application.Current?.MainWindow;
        return main is { IsLoaded: true } ? main : null;
    }

    private T OnUi<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        try
        {
            return dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A user-interaction dialog failed.");
            return default!;
        }
    }
}
