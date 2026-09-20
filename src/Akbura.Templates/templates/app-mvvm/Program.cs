using Akbura.Engine;
using AkburaTemplateNamespace.Infrastructure;
using Avalonia;

#if UseReactiveUI
using ReactiveUI.Avalonia;
#endif

namespace AkburaTemplateNamespace;

internal static class Program
{
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
            AppServices.Dispose();
        }
#else
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#endif
    }

    // Also used by the Avalonia designer. Service registration is lazy and idempotent.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
#if UseReactiveUI
            .UseReactiveUI(_ => { })
#endif
#if UseDI
            .UseAkbura(akbura =>
            {
                // Preview setup should not instantiate the runtime container.
                if (!Avalonia.Controls.Design.IsDesignMode)
                {
                    akbura.WithServiceProvider(AppServices.ServiceProvider);
                }
            });
#else
            .UseAkbura();
#endif
}
