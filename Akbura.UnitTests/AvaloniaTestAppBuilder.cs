using Avalonia;
using Avalonia.Headless;

namespace Akbura.UnitTests;

public static class AvaloniaTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
