using Akbura.Engine;
using Akbura.FeatureGallery;
using Avalonia;
using Avalonia.Browser;
using System.Runtime.Versioning;
using System.Threading.Tasks;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .UseAkbura()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}