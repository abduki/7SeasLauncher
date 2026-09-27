using System.Windows;
using SevenSeas.Core.Abstractions;
using SevenSeas.Launcher.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace SevenSeas.Launcher.Services;

/// <summary>
/// Owns the little windows used for pages that a download link opens in a new window, and makes
/// sure none of them outlive the app.
/// </summary>
public sealed class DownloadPopupService
{
    private readonly DownloadInterceptor _interceptor;
    private readonly StatusService _status;
    private readonly ShellNavigator _navigator;
    private readonly ISchemeLoader _bookmarks;
    private readonly ILogger<DownloadPopupService> _logger;
    private readonly ILogger<DownloadPopupWindow> _popupLogger;
    private readonly List<DownloadPopupWindow> _open = new();

    private CoreWebView2Environment? _environment;
    private Window? _owner;

    public DownloadPopupService(
        DownloadInterceptor interceptor,
        StatusService status,
        ShellNavigator navigator,
        ISchemeLoader bookmarks,
        ILogger<DownloadPopupService>? logger = null,
        ILogger<DownloadPopupWindow>? popupLogger = null)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _bookmarks = bookmarks ?? throw new ArgumentNullException(nameof(bookmarks));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadPopupService>.Instance;
        _popupLogger = popupLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DownloadPopupWindow>.Instance;
    }

    /// <summary>Called once the main browser has an environment, so popups share its session.</summary>
    public void Initialize(CoreWebView2Environment environment, Window? owner)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _owner = owner;
    }

    public bool IsReady => _environment is not null;

    /// <summary>
    /// Creates and shows a popup window, returning the <see cref="CoreWebView2"/> the caller must hand
    /// to <c>NewWindowRequested.NewWindow</c>. We deliberately do not navigate it ourselves: letting
    /// WebView2 perform the window open is what preserves <c>window.opener</c> and the referrer, which
    /// ad gateways check before they will serve a download link.
    /// </summary>
    public async Task<CoreWebView2?> OpenAsync(string uri)
    {
        if (_environment is null)
        {
            _logger.LogWarning("Popup requested before the browser was ready: {Uri}", uri);
            return null;
        }

        try
        {
            var popup = new DownloadPopupWindow(_environment, _interceptor, _status, _navigator, _bookmarks, uri, _popupLogger);

            if (_owner is { IsLoaded: true })
            {
                popup.Owner = _owner;
            }

            popup.Closed += (_, _) => _open.Remove(popup);
            _open.Add(popup);

            // Shown first so the WebView2 control has a visual tree to initialise in.
            popup.Show();

            var core = await popup.InitializeAsync();
            if (core is null)
            {
                popup.Close();
            }

            return core;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open a download popup for {Uri}.", uri);
            return null;
        }
    }

    /// <summary>Closes every open popup, used on shutdown so nothing is left behind.</summary>
    public void CloseAll()
    {
        foreach (var popup in _open.ToList())
        {
            try
            {
                popup.ForceClose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close a download popup.");
            }
        }

        _open.Clear();
    }
}
