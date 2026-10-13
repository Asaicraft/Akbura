using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Akbura.HotReloadIntegrationTests.AvaloniaTestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]

namespace Akbura.HotReloadIntegrationTests;

public static class AvaloniaTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
