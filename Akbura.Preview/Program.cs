using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Akbura.Preview;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Required by the Avalonia IDE previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure<PreviewApplication>()
            .UsePlatformDetect();
    }
}
