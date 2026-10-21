using Akbura.Engine;
using Akbura.FeatureGallery;
using Akbura.FeatureGallery.Browser.Routing;
using Akbura.FeatureGallery.Routing;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Threading;
using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;

[SupportedOSPlatform("browser")]
internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("The browser bootstrap must supply the gallery base URL.");
        var bridge = await JsBrowserUrl.CreateAsync(args[0]);
        BrowserRouter router;
        try { router = new BrowserRouter(bridge, DispatchToUi); }
        catch { bridge.Dispose(); throw; }
        try
        {
            await BuildAvaloniaApp()
                .WithInterFont()
                .UseGalleryAkbura(router)
                .StartBrowserAppAsync("out", new BrowserPlatformOptions
                {
                    FrameworkAssetPathResolver = file => new Uri(new Uri(bridge.BaseUrl), "_framework/" + file).AbsoluteUri,
                });
        }
        catch { router.Dispose(); throw; }
        // StartBrowserAppAsync completes after setup, not after app shutdown.
        // The engine's application-scoped provider retains the router thereafter.
    }

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
