using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Akbura.FeatureGallery.Routing;

/// <summary>History-aware logical routing, with no dependency on JavaScript APIs.</summary>
public sealed class BrowserRouter : ObservableObject, IRouter, IDisposable
{
    private readonly IJsBrowserUrl _browser;
    private readonly Uri _baseUrl;
    private readonly Action<Action> _dispatch;
    private string _url;
    private bool _disposed;

    /// <param name="dispatch">Schedules browser callbacks on the host's UI thread.</param>
    public BrowserRouter(IJsBrowserUrl browser, Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(browser);
        _browser = browser;
        _dispatch = dispatch ?? (action => action());
        _baseUrl = ValidateBaseUrl(browser.BaseUrl);
        var initial = new Uri(browser.CurrentUrl, UriKind.Absolute);
        _url = ReadRoute(initial);
        // Preserve startup query/fragment metadata while canonicalizing only
        // the pathname. Replacing does not manufacture a Back history entry.
        var canonical = BrowserUrl(_url);
        if (!string.Equals(initial.AbsolutePath, canonical.AbsolutePath, StringComparison.Ordinal))
            browser.ReplaceUrl(canonical.GetLeftPart(UriPartial.Path) + initial.Query + initial.Fragment);
        browser.UrlChanged += OnUrlChanged;
    }

    public string Url
    {
        get => _url;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var route = GalleryRoutePath.Normalize(value);
            if (route == _url) return;
            // pushState does not emit popstate. Commit logical state explicitly,
            // only after the browser accepted the navigation.
            _browser.PushUrl(BrowserUrl(route).AbsoluteUri);
            SetProperty(ref _url, route);
        }
    }

    private Uri BrowserUrl(string route) => new(_baseUrl, route.TrimStart('/') + "/");

    private string ReadRoute(Uri location)
    {
        if (!string.Equals(location.GetLeftPart(UriPartial.Authority), _baseUrl.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The browser URL is outside the configured gallery origin.");
        var path = location.AbsolutePath;
        var prefix = _baseUrl.AbsolutePath;
        if (path == prefix.TrimEnd('/')) return GalleryRoutePath.Overview;
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("The browser URL is outside the configured gallery base path.");
        return GalleryRoutePath.Normalize(path[prefix.Length..]);
    }

    private static Uri ValidateBaseUrl(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("The gallery base must be an absolute HTTP(S) application URL.", nameof(value));
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private void OnUrlChanged(object? sender, string url) => _dispatch(() =>
    {
        if (_disposed || !Uri.TryCreate(url, UriKind.Absolute, out var location)) return;
        string route;
        try { route = ReadRoute(location); }
        catch (ArgumentException) { return; } // A location outside this application is not an internal route.
        SetProperty(ref _url, route, nameof(Url)); // Never push/replace in response to browser history.
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _browser.UrlChanged -= OnUrlChanged;
        _browser.Dispose();
    }
}
