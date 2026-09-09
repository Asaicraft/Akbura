using Akbura.Engine;
using Avalonia;

#if UseDI
using AkburaTemplateNamespace.Services;
#endif

#if UseMicrosoftDI
using Microsoft.Extensions.DependencyInjection;
#endif

#if UseSplat
using AkburaTemplateNamespace.Infrastructure;
using Splat;
#endif

namespace AkburaTemplateNamespace;

internal static class Program
{
#if UseMicrosoftDI
    private static ServiceProvider? s_services;
#endif

    [STAThread]
    public static void Main(string[] args)
    {
#if UseMicrosoftDI
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            s_services?.Dispose();
            s_services = null;
        }
#else
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#endif
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
#if UseMicrosoftDI
            .UseAkbura(akbura =>
            {
                s_services ??= CreateServiceProvider();

                akbura.WithServiceProvider(s_services);
            });
#elif UseSplat
            .UseAkbura(akbura =>
            {
                Locator.CurrentMutable.RegisterConstant<IGreetingService>(
                    new GreetingService());

                akbura.WithServiceProvider(new SplatServiceProvider());
            });
#else
            .UseAkbura();
#endif

#if UseMicrosoftDI
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IGreetingService, GreetingService>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
#endif
}
