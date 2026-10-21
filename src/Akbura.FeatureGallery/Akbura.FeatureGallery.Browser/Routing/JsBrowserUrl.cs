using Akbura.FeatureGallery.Routing;
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Akbura.FeatureGallery.Browser.Routing;

[SupportedOSPlatform("browser")]
internal sealed partial class JsBrowserUrl : IJsBrowserUrl
{
    private const string Module = "AkburaGalleryBrowserUrl";
    private readonly int _subscription;
    private bool _disposed;

    private JsBrowserUrl(string baseUrl)
    {
        BaseUrl = baseUrl;
        _subscription = Subscribe(OnUrlChanged);
    }

    public static async Task<JsBrowserUrl> CreateAsync(string baseUrl)
    {
        var moduleUrl = new Uri(new Uri(baseUrl, UriKind.Absolute), "browser-router.js");
        await JSHost.ImportAsync(Module, moduleUrl.AbsoluteUri);
        return new JsBrowserUrl(baseUrl);
    }

    public string BaseUrl { get; }
    public string CurrentUrl => GetCurrentUrl();
    public event EventHandler<string>? UrlChanged;
    public void PushUrl(string url) => Push(url);
    public void ReplaceUrl(string url) => Replace(url);
    private void OnUrlChanged(string url) { if (!_disposed) UrlChanged?.Invoke(this, url); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unsubscribe(_subscription);
        UrlChanged = null;
    }

    [JSImport("getCurrentUrl", Module)]
    private static partial string GetCurrentUrl();
    [JSImport("pushUrl", Module)]
    private static partial void Push(string url);
    [JSImport("replaceUrl", Module)]
    private static partial void Replace(string url);
    [JSImport("subscribe", Module)]
    private static partial int Subscribe([JSMarshalAs<JSType.Function<JSType.String>>] Action<string> callback);
    [JSImport("unsubscribe", Module)]
    private static partial void Unsubscribe(int subscription);
}
