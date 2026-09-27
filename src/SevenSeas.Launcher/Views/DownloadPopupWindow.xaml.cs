using System.Windows;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;

namespace SevenSeas.Launcher.Views;

/// <summary>
/// A small window for pages a download link opened with target="_blank" or window.open().
/// <para>
/// The page is loaded by WebView2 itself through <c>NewWindowRequested.NewWindow</c>, not by us calling
/// Navigate. That matters: a host-initiated navigation has no <c>window.opener</c> and no <c>Referer</c>,
/// which makes ad gateways treat it as a pasted "direct link" and bounce it. Hosting the window properly
/// keeps the opener relationship and the referrer, so a genuine click-through looks genuine.
/// </para>
/// The window closes itself once the download it triggered finishes.
/// </summary>
public partial class DownloadPopupWindow : FluentWindow
{
    private readonly CoreWebView2Environment _environment;
    private readonly DownloadInterceptor _interceptor;
    private readonly StatusService _status;
    private readonly ShellNavigator _navigator;
    private readonly ISchemeLoader _bookmarks;
    private readonly string _targetUri;
    private readonly ILogger<DownloadPopupWindow> _logger;

    /// <summary>
    /// WebView2 does not wire up <c>window.opener</c> for host-created windows
    /// (MicrosoftEdge/WebView2Feedback#910), so a page opened this way looks like it was pasted
    /// directly. Ad gateways such as bzzhr.to check for it, alongside the referrer, to decide whether
    /// to serve the download or bounce you to the referring site's home page. Supplying the same
    /// signal a genuine <c>window.open()</c> produces makes a real click-through look real.
    /// </summary>
    private const string OpenerShimScript = """
        (function () {
          try {
            if (!window.opener) {
              window.opener = {
                closed: false,
                postMessage: function () { },
                focus: function () { },
                close: function () { }
              };
            }
          } catch (e) { }
        })();
        """;

    private CoreWebView2DownloadOperation? _operation;
    private bool _closed;
    private bool _allowClose;

    public DownloadPopupWindow(
        CoreWebView2Environment environment,
        DownloadInterceptor interceptor,
        StatusService status,
        ShellNavigator navigator,
        ISchemeLoader bookmarks,
        string targetUri,
        ILogger<DownloadPopupWindow>? logger = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _targetUri = targetUri ?? throw new ArgumentNullException(nameof(targetUri));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadPopupWindow>.Instance;

        InitializeComponent();

        var host = SiteNaming.NormalizedHost(targetUri) ?? targetUri;
        Title = host;
        PopupTitleBar.Title = host;
        StatusText.Text = $"Opening {host}…";

        Closed += OnClosed;
    }

    /// <summary>
    /// Prepares the WebView2 so the caller can hand it to <c>NewWindowRequested.NewWindow</c>.
    /// Returns null when the control could not start.
    /// </summary>
    public async Task<CoreWebView2?> InitializeAsync()
    {
        try
        {
            if (!IsLoaded)
            {
                var loaded = new TaskCompletionSource();
                void OnFirstLoad(object? sender, RoutedEventArgs e)
                {
                    Loaded -= OnFirstLoad;
                    loaded.TrySetResult();
                }

                Loaded += OnFirstLoad;
                await loaded.Task;
            }

            // Same environment and user data folder as the main browser, so cookies, session and
            // the download interceptor all carry over.
            await Web.EnsureCoreWebView2Async(_environment);

            var core = Web.CoreWebView2;
            if (core is null)
            {
                return null;
            }

            _interceptor.Attach(core);
            core.DownloadStarting += OnDownloadStarting;
            core.WindowCloseRequested += OnWindowCloseRequested;
            core.SourceChanged += (_, _) => AddressText.Text = Web.Source?.ToString() ?? string.Empty;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;

            // A popup opened from this popup just reuses this window.
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                core.Navigate(args.Uri);
            };

            // Runs before any page script, so the check sees it.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(OpenerShimScript);

            return core;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download popup failed to initialise.");
            StatusText.Text = "Could not open this page.";
            return null;
        }
    }

    /// <summary>Add the page being browsed here as a bookmark, without going back to the main window.</summary>
    private void OnSavePageClick(object sender, RoutedEventArgs e)
    {
        var url = Web.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Nothing to save yet.";
            return;
        }

        var scheme = new SiteScheme
        {
            Name = SiteNaming.DisplayName(Web.CoreWebView2?.DocumentTitle, url),
            BaseUrl = SiteNaming.SiteRoot(url) ?? url,
            Notes = $"Saved from {url}",
        };

        var editor = SchemeEditorWindow.ForAdd(
            scheme,
            "Saved from the window you are browsing. Add a search URL if you also want to search this site.");

        if (IsLoaded)
        {
            editor.Owner = this;
        }

        if (editor.ShowDialog() != true || editor.Result is null)
        {
            return;
        }

        try
        {
            var saved = _bookmarks.Save(editor.Result);
            _navigator.NotifyBookmarksChanged();
            StatusText.Text = $"Saved '{saved.Name}' to your bookmarks.";
            _status.Report(saved.CanSearch
                ? $"Saved '{saved.Name}'. You can search it."
                : $"Saved '{saved.Name}' as a bookmark.");
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    /// <summary>Hand the current page to the main window, where the bookmarks bar lives.</summary>
    private void OnOpenInMainClick(object sender, RoutedEventArgs e)
    {
        var url = Web.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _navigator.OpenUrlInMainWindow(url);
        _status.Report("Opened in the main window.");
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Progress.Visibility = Visibility.Collapsed;

        if (_operation is null)
        {
            StatusText.Text = $"Loading {SiteNaming.NormalizedHost(e.Uri) ?? e.Uri}…";
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            StatusText.Text = $"Could not load the page ({e.WebErrorStatus}).";
        }
        else if (_operation is null)
        {
            // Loaded fine and no download yet: the page may still start one.
            StatusText.Text = "Waiting for the download to start…";
        }
    }

    /// <summary>
    /// The page asked to close itself (window.close()), which download interstitials often do.
    /// </summary>
    private void OnWindowCloseRequested(object? sender, object e)
    {
        _logger.LogInformation("Popup page requested close.");
        Close();
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var operation = e.DownloadOperation;

        // The shared interceptor has already redirected the file into Temp\downloads.
        var fileName = Path.GetFileName(operation.ResultFilePath);
        StatusText.Text = $"Downloading {fileName}…";
        Progress.Visibility = Visibility.Visible;

        _operation = operation;
        operation.BytesReceivedChanged += OnBytesReceivedChanged;
        operation.StateChanged += OnDownloadStateChanged;

        _logger.LogInformation("Download started from a popup: {File}", fileName);
    }

    private void OnBytesReceivedChanged(object? sender, object e)
    {
        if (_operation is null)
        {
            return;
        }

        var total = _operation.TotalBytesToReceive ?? 0;
        Progress.Value = total > 0
            ? Math.Clamp(_operation.BytesReceived * 100.0 / total, 0, 100)
            : 0;
    }

    private void OnDownloadStateChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2DownloadOperation operation)
        {
            return;
        }

        switch (operation.State)
        {
            case CoreWebView2DownloadState.Completed:
                Detach(operation);
                _allowClose = true;
                // Only close once the file is safely down; closing earlier can cancel it.
                StatusText.Text = "Downloaded — closing…";
                Progress.Value = 100;
                CloseShortly();
                break;

            case CoreWebView2DownloadState.Interrupted:
                Detach(operation);
                StatusText.Text = $"Download interrupted ({operation.InterruptReason}). You can close this window.";
                Progress.Visibility = Visibility.Collapsed;
                _logger.LogWarning("Popup download interrupted: {Reason}", operation.InterruptReason);
                break;
        }
    }

    private void Detach(CoreWebView2DownloadOperation operation)
    {
        operation.BytesReceivedChanged -= OnBytesReceivedChanged;
        operation.StateChanged -= OnDownloadStateChanged;
        _operation = null;
    }

    /// <summary>Small pause so "Downloaded" is actually visible before the window disappears.</summary>
    private async void CloseShortly()
    {
        if (_closed)
        {
            return;
        }

        await Task.Delay(900);
        if (!_closed)
        {
            _allowClose = true;
            Close();
        }
    }

    /// <summary>
    /// Closing the window would tear down the WebView2 and cancel a download that is still running,
    /// so while one is in flight the window is hidden instead. It is closed for real once the file
    /// is safely down, or when the app shuts down.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && _operation is not null && !_closed)
        {
            e.Cancel = true;
            Hide();
            _logger.LogInformation("Download window dismissed while a download is running; keeping it alive.");
            _status.Report("Download continues in the background.");
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Closes regardless of an in-flight download, used on shutdown.</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _logger.LogInformation("Download window closed.");
        _status.Report("Download window closed.");
    }
}
