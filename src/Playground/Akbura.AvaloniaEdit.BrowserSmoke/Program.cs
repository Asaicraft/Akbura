using Avalonia;
using Avalonia.Browser;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

internal static class Program
{
    private static Task Main(string[] args) => AppBuilder.Configure<SmokeApp>().StartBrowserAppAsync("out");
}
