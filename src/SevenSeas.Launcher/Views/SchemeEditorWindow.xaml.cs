using System.Windows;
using System.Windows.Controls;
using SevenSeas.Core.Models;
using Wpf.Ui.Controls;

namespace SevenSeas.Launcher.Views;

/// <summary>
/// Add/edit dialog for a site scheme. The caller persists the
/// returned <see cref="SiteScheme"/> through the Scheme Loader.
/// </summary>
public partial class SchemeEditorWindow : FluentWindow
{
    private SiteScheme? _original;

    private SchemeEditorWindow()
    {
        InitializeComponent();
        UpdatePreview();
    }

    /// <summary>Opens the dialog for a brand-new site, optionally pre-filled from the current page.</summary>
    public static SchemeEditorWindow ForAdd(SiteScheme? prefill = null, string? banner = null)
    {
        var window = new SchemeEditorWindow
        {
            Title = "Add website",
        };
        window.HeadingText.Text = "Add website";
        window.WindowTitleBar.Title = "Add website";

        if (prefill is not null)
        {
            window.NameBox.Text = prefill.Name;
            window.BaseUrlBox.Text = prefill.BaseUrl;
            window.SearchTemplateBox.Text = prefill.SearchUrlTemplate;
            window.PasswordHintBox.Text = prefill.ArchivePasswordHint ?? string.Empty;
            window.NotesBox.Text = prefill.Notes ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(banner))
        {
            window.BannerText.Text = banner;
            window.Banner.Visibility = Visibility.Visible;
        }

        window.UpdatePreview();
        return window;
    }

    /// <summary>Opens the dialog for an existing site.</summary>
    public static SchemeEditorWindow ForEdit(SiteScheme existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var window = new SchemeEditorWindow
        {
            Title = "Edit website",
            _original = existing,
        };
        window.HeadingText.Text = "Edit website";
        window.WindowTitleBar.Title = "Edit website";
        window.NameBox.Text = existing.Name;
        window.BaseUrlBox.Text = existing.BaseUrl;
        window.SearchTemplateBox.Text = existing.SearchUrlTemplate;
        window.PasswordHintBox.Text = existing.ArchivePasswordHint ?? string.Empty;
        window.NotesBox.Text = existing.Notes ?? string.Empty;
        window.UpdatePreview();
        return window;
    }

    /// <summary>The edited scheme, or null when cancelled.</summary>
    public SiteScheme? Result { get; private set; }

    private void OnInputChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText is null)
        {
            return;
        }

        var template = SearchTemplateBox.Text.Trim();
        if (template.Length == 0)
        {
            PreviewText.Text = "Fill in the search URL template to see an example.";
            PreviewText.Opacity = 0.6;
            return;
        }

        if (!template.Contains("{query}", StringComparison.Ordinal))
        {
            PreviewText.Text = "Add {query} where the search term belongs \u2014 without it the site cannot be searched.";
            PreviewText.Opacity = 0.9;
            return;
        }

        // Show what an actual search would request.
        PreviewText.Text = template.Replace("{query}", Uri.EscapeDataString("zelda"), StringComparison.Ordinal);
        PreviewText.Opacity = 1.0;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var candidate = new SiteScheme
        {
            Name = NameBox.Text.Trim(),
            BaseUrl = BaseUrlBox.Text.Trim(),
            SearchUrlTemplate = SearchTemplateBox.Text.Trim(),
            ArchivePasswordHint = string.IsNullOrWhiteSpace(PasswordHintBox.Text) ? null : PasswordHintBox.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            SourceFile = _original?.SourceFile,
        };

        if (!candidate.TryValidate(out var error))
        {
            ErrorText.Text = error;
            ErrorPanel.Visibility = Visibility.Visible;
            FocusOffendingField(error);
            return;
        }

        ErrorPanel.Visibility = Visibility.Collapsed;
        Result = candidate;
        DialogResult = true;
        Close();
    }

    /// <summary>Puts the caret in whichever field the validator complained about.</summary>
    private void FocusOffendingField(string error)
    {
        if (error.Contains("name", StringComparison.OrdinalIgnoreCase))
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }
        else if (error.Contains("home page", StringComparison.OrdinalIgnoreCase))
        {
            BaseUrlBox.Focus();
            BaseUrlBox.SelectAll();
        }
        else if (error.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            SearchTemplateBox.Focus();
            SearchTemplateBox.SelectAll();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
