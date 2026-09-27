using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SevenSeas.Core.Abstractions;
using SevenSeas.Core.Models;
using SevenSeas.Core.Services;
using SevenSeas.Launcher.Services;
using SevenSeas.Launcher.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace SevenSeas.Launcher.Views;

/// <summary>
/// The embedded browser. Owns the WebView2 control, drives the bookmarks
/// bar, and hands every download to the download interceptor.
/// </summary>
public partial class BrowserView : UserControl
{
    /// <summary>
    /// Looks for the page's search box so saving a bookmark can fill in a working search URL.
    /// Returns a JSON string, or an empty string when nothing convincing is found.
    /// </summary>
    private const string SearchProbeScript = """
        (function () {
          try {
            var forms = Array.prototype.slice.call(document.querySelectorAll('form'));
            for (var i = 0; i < forms.length; i++) {
              var f = forms[i];
              var inputs = Array.prototype.slice.call(f.querySelectorAll('input'));
              for (var j = 0; j < inputs.length; j++) {
                var inp = inputs[j];
                var name = inp.getAttribute('name');
                if (!name) { continue; }
                var type = (inp.getAttribute('type') || 'text').toLowerCase();
                if (type === 'search' || /^(q|s|query|search|keyword|k)$/i.test(name)) {
                  var action = f.getAttribute('action') || f.action || '';
                  if (!action) { action = location.origin + location.pathname; }
                  return JSON.stringify({ action: action, param: name });
                }
              }
            }
            var bare = document.querySelector('input[type=search][name], input[name=q], input[name=query], input[name=s]');
            if (bare) {
              return JSON.stringify({ action: location.origin + '/search', param: bare.getAttribute('name') });
            }
          } catch (e) { }
          return '';
        })();
        """;

    private readonly BrowserViewModel _viewModel;
    private readonly DownloadInterceptor _interceptor;
    private readonly DownloadsFolderWatcher _watcher;
    private readonly DownloadIntakeService _intake;
    private readonly PipelineWorker _pipeline;
    private readonly ISchemeLoader _bookmarks;
    private readonly DownloadPopupService _popups;
    private readonly ILogger<BrowserView> _logger;
    private bool _initialized;

    public BrowserView(
        BrowserViewModel viewModel,
        DownloadInterceptor interceptor,
        DownloadsFolderWatcher watcher,
        DownloadIntakeService intake,
        PipelineWorker pipeline,
        ISchemeLoader bookmarks,
        DownloadPopupService popups,
        ShellNavigator navigator,
        ILogger<BrowserView>? logger = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _intake = intake ?? throw new ArgumentNullException(nameof(intake));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _popups = popups ?? throw new ArgumentNullException(nameof(popups));
        navigator.RequestUrl += NavigateAddress;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BrowserView>.Instance;

        InitializeComponent();
        DataContext = _viewModel;
        BookmarksFolderText.Text = _bookmarks.SchemesDirectory;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserViewModel.HasBookmarks))
            {
                UpdateEmptyState();
            }
        };

        UpdateEmptyState();
        Loaded += OnLoaded;
    }

    private void UpdateEmptyState()
        => EmptyState.Visibility = _viewModel.HasBookmarks ? Visibility.Collapsed : Visibility.Visible;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        _watcher.FileDetected += OnUnmanagedDownloadDetected;
        _watcher.Start();

        try
        {
            var userDataFolder = Path.Combine(AppPaths.AppDataRoot, "webview2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await Web.EnsureCoreWebView2Async(environment);

            // Download popups share this environment so cookies and the download interceptor carry over.
            _popups.Initialize(environment, Window.GetWindow(this));

            _interceptor.Attach(Web.CoreWebView2);
            Web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            Web.CoreWebView2.SourceChanged += OnSourceChanged;
            Web.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            var start = _viewModel.SelectedBookmark?.Address
                        ?? _viewModel.Bookmarks.FirstOrDefault()?.Address;

            if (!string.IsNullOrWhiteSpace(start))
            {
                NavigateAddress(start);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 failed to initialise.");
            Dialogs.Inform(
                "WebView2 unavailable",
                "The embedded browser could not start.\n\n" +
                "Install the Microsoft Edge WebView2 Runtime (Evergreen Bootstrapper), then restart 7SeasLauncher.\n\n" +
                ex.Message);
        }
    }

    // ---------- The bookmarks bar ----------

    /// <summary>Clicking a bookmark opens it — this is the whole point of the bar.</summary>
    private void OnBookmarkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BookmarkItemViewModel bookmark })
        {
            return;
        }

        _viewModel.SelectBookmark(bookmark);
        NavigateAddress(bookmark.Address);
    }

    /// <summary>
    /// One action for saving: if the page is already bookmarked this edits it, otherwise it creates a
    /// new bookmark pre-filled from the page (name, address and, when found, its search box).
    /// </summary>
    private async void OnSavePageClick(object sender, RoutedEventArgs e)
    {
        var currentUrl = Web.Source?.ToString();
        var existing = _viewModel.FindBookmark(currentUrl);

        if (existing is not null)
        {
            var edit = SchemeEditorWindow.ForEdit(existing.Model);
            edit.Owner = Window.GetWindow(this);
            ShowEditorAndSave(edit, existing.Name);
            return;
        }

        var (scheme, banner) = await BuildBookmarkFromCurrentPageAsync();
        if (scheme is null)
        {
            _viewModel.Report("Browse to a site first, then click Save this page.");
            return;
        }

        var editor = SchemeEditorWindow.ForAdd(scheme, banner);
        editor.Owner = Window.GetWindow(this);
        ShowEditorAndSave(editor, originalName: null);
    }

    private void ShowEditorAndSave(SchemeEditorWindow editor, string? originalName)
    {
        if (editor.ShowDialog() != true || editor.Result is null)
        {
            return;
        }

        try
        {
            var saved = _viewModel.SaveBookmark(editor.Result, originalName);
            _viewModel.Report(saved.CanSearch
                ? $"Saved '{saved.Name}'. You can search it now."
                : $"Saved '{saved.Name}' as a bookmark. Add a search URL to search it.");
            UpdateEmptyState();
        }
        catch (ArgumentException ex)
        {
            Dialogs.Inform("Could not save the bookmark", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving a bookmark failed.");
            Dialogs.Inform("Could not save the bookmark", ex.Message);
        }
    }

    /// <summary>
    /// Reads the page the user is on and proposes a bookmark: name from the title/host, address from
    /// the site root, and a search URL discovered in the page's search form when there is one.
    /// </summary>
    private async Task<(SiteScheme? Scheme, string? Banner)> BuildBookmarkFromCurrentPageAsync()
    {
        var url = Web.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var title = Web.CoreWebView2?.DocumentTitle;
        var name = SiteNaming.DisplayName(title, url);
        var root = SiteNaming.SiteRoot(url) ?? url;

        var (action, parameter) = await ProbeSearchFormAsync(url);
        var template = SiteNaming.BuildSearchTemplate(action, parameter);

        string? banner;
        if (string.IsNullOrWhiteSpace(template))
        {
            // Still a perfectly good bookmark — it just cannot be searched yet.
            banner = "Saved as a bookmark you can browse. 7SeasLauncher could not find a search box on this page, " +
                     "so searching it will not work until you add a search URL below (leave it empty to browse only).";
        }
        else
        {
            banner = "Pre-filled from the page you are viewing. 7SeasLauncher found the search box and the site name \u2014 check them, then save.";
        }

        var scheme = new SiteScheme
        {
            Name = name,
            BaseUrl = root,
            SearchUrlTemplate = string.IsNullOrWhiteSpace(template) ? null : template,
            Notes = $"Saved from {url}",
        };

        return (scheme, banner);
    }

    private async Task<(string? Action, string? Parameter)> ProbeSearchFormAsync(string pageUrl)
    {
        try
        {
            if (Web.CoreWebView2 is null)
            {
                return (null, null);
            }

            var raw = await Web.CoreWebView2.ExecuteScriptAsync(SearchProbeScript);
            if (string.IsNullOrWhiteSpace(raw) || raw == "null")
            {
                return (null, null);
            }

            var json = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrWhiteSpace(json))
            {
                return (null, null);
            }

            using var document = JsonDocument.Parse(json);
            var action = document.RootElement.TryGetProperty("action", out var a) ? a.GetString() : null;
            var parameter = document.RootElement.TryGetProperty("param", out var p) ? p.GetString() : null;

            if (!string.IsNullOrWhiteSpace(action) &&
                Uri.TryCreate(new Uri(pageUrl), action, out var absolute))
            {
                action = absolute.ToString();
            }

            return (action, parameter);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not probe the page for a search form.");
            return (null, null);
        }
    }

    // ---------- Unmanaged downloads (fallback) ----------

    private void OnUnmanagedDownloadDetected(string filePath)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => OnUnmanagedDownloadDetected(filePath));
            return;
        }

        if (!Dialogs.Confirm(
                "Download detected",
                $"A download landed in your Downloads folder:\n\n{Path.GetFileName(filePath)}\n\n" +
                "Do you want 7SeasLauncher to process it?",
                "Process it",
                "Ignore"))
        {
            return;
        }

        var gameName = TitleCleaner.Clean(Path.GetFileName(filePath));
        var job = _intake.BeginDownload(gameName, null, filePath);
        _intake.MarkDownloadCompleted(job.Id, filePath);
        _pipeline.NotifyJobAvailable();
        _logger.LogInformation("Queued detected download {File} as job {JobId}.", filePath, job.Id);
    }

    // ---------- Navigation ----------

    /// <summary>
    /// A link that opens a new page gets its own small window rather than hijacking the main view.
    /// Download sites send you to an interstitial that way, and this keeps your place on the game page.
    /// The window closes itself once the download it triggers finishes.
    /// </summary>
    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!e.IsUserInitiated)
        {
            // Script-triggered popups are almost always adverts; swallowing them keeps the app quiet.
            e.Handled = true;
            _logger.LogInformation("Ignored a non user-initiated new window request for {Uri}.", e.Uri);
            return;
        }

        // Deferring lets us finish asynchronously and still hand WebView2 a real window.
        // The opener stays blocked on a pending WindowProxy until Complete() is called, which is
        // exactly the state a normal window.open() produces.
        var deferral = e.GetDeferral();

        try
        {
            _logger.LogInformation("Opening {Uri} in a download window.", e.Uri);

            var core = await _popups.OpenAsync(e.Uri);

            if (core is not null)
            {
                // The browser performs the navigation, so window.opener and the referrer survive.
                e.NewWindow = core;
            }
            else
            {
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open a download window for {Uri}.", e.Uri);
            e.Handled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        => _viewModel.Address = Web.Source?.ToString() ?? string.Empty;

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _viewModel.IsLoading = false;
        _viewModel.CanGoBack = Web.CoreWebView2?.CanGoBack ?? false;
        _viewModel.CanGoForward = Web.CoreWebView2?.CanGoForward ?? false;
        _viewModel.Address = Web.Source?.ToString() ?? string.Empty;

        BackButton.IsEnabled = _viewModel.CanGoBack;
        ForwardButton.IsEnabled = _viewModel.CanGoForward;

        if (e.IsSuccess)
        {
            // Highlight whichever bookmark owns this page. This never navigates, so no loop.
            _viewModel.SelectForUrl(Web.Source?.ToString());
        }
        else
        {
            _logger.LogWarning("Navigation failed: {Status}", e.WebErrorStatus);
        }
    }

    private void NavigateAddress(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }

        _viewModel.Address = url;
        Web.CoreWebView2?.Navigate(url);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoBack == true)
        {
            Web.CoreWebView2.GoBack();
        }
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2?.CanGoForward == true)
        {
            Web.CoreWebView2.GoForward();
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e) => Web.CoreWebView2?.Reload();

    /// <summary>Home opens the home page of the selected bookmark.</summary>
    private void OnHomeClick(object sender, RoutedEventArgs e)
    {
        var target = _viewModel.ResolveHomeUrl(Web.Source?.ToString());

        if (string.IsNullOrWhiteSpace(target))
        {
            _viewModel.Report("Save a bookmark first.");
            return;
        }

        NavigateAddress(target);
    }

    private void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateAddress(AddressBox.Text);
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        var selected = _viewModel.SelectedBookmark;

        if (selected is null)
        {
            _viewModel.Report("Open a bookmark first, or click Save this page to add one.");
            return;
        }

        if (!selected.CanSearch)
        {
            _viewModel.Report($"'{selected.Name}' has no search URL yet. Click Save this page to add one.");
            return;
        }

        var url = _viewModel.BuildSearchUrl();
        if (url is null)
        {
            _viewModel.Report("Type a game name first.");
            return;
        }

        NavigateAddress(url);
    }
}
