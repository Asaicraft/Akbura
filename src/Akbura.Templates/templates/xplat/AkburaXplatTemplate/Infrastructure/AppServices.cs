using AkburaTemplateNamespace.Services;
using AkburaTemplateNamespace.ViewModels;

#if UseMicrosoftDI
using Microsoft.Extensions.DependencyInjection;
#endif

#if UseSplat
using Splat;
#endif

namespace AkburaTemplateNamespace.Infrastructure;

/// <summary>One composition root shared by all platform hosts and Activity recreations.</summary>
internal sealed class AppServices
{
    private static readonly Lazy<AppServices> s_current = new(() => new AppServices());

#if UseMicrosoftDI
    private readonly ServiceProvider _provider;
#elif UseSplat
    private readonly SplatServiceProvider _provider;
#endif

    private AppServices()
    {
#if UseMicrosoftDI
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, GreetingService>();
        services.AddSingleton<MainViewModel>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        MainViewModel = _provider.GetRequiredService<MainViewModel>();
#elif UseSplat
        var greetingService = new GreetingService();
        MainViewModel = new MainViewModel(greetingService);

        Locator.CurrentMutable.RegisterConstant<IGreetingService>(greetingService);
        Locator.CurrentMutable.RegisterConstant(MainViewModel);
        _provider = new SplatServiceProvider();
#else
        MainViewModel = new MainViewModel(new GreetingService());
#endif
    }

    public static AppServices Current => s_current.Value;

    public MainViewModel MainViewModel { get; }

#if UseDI
    public IServiceProvider ServiceProvider => _provider;
#endif

    public static void DisposeOwnedServices()
    {
#if UseMicrosoftDI
        if (s_current.IsValueCreated)
        {
            s_current.Value._provider.Dispose();
        }
#endif
    }
}
