using Akbura.Engine;
using Avalonia;

namespace Akbura.Playground;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PlaygroundApp>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .UseAkbura();
}
