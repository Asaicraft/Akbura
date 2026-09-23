using AkburaTemplateNamespace.Services;
using AkburaTemplateNamespace.ViewModels;

#if UseMicrosoftDI
using Microsoft.Extensions.DependencyInjection;
#endif

#if UseSplat
using Splat;
#endif

namespace AkburaTemplateNamespace.Infrastructure;

internal static class AppServices
{
#if UseMicrosoftDI
    private static readonly Lazy<ServiceProvider> s_provider = new(CreateServiceProvider);

    public static IServiceProvider ServiceProvider => s_provider.Value;

    public static MainViewModel MainViewModel =>
        s_provider.Value.GetRequiredService<MainViewModel>();

    public static void Dispose()
    {
        if (s_provider.IsValueCreated)
        {
            s_provider.Value.Dispose();
        }
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreetingService, GreetingService>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
#elif UseSplat
    private static readonly Lazy<SplatServiceProvider> s_provider = new(CreateServiceProvider);

    public static IServiceProvider ServiceProvider => s_provider.Value;

    public static MainViewModel MainViewModel
    {
        get
        {
            _ = s_provider.Value;
            return Locator.Current.GetService<MainViewModel>()
                ?? throw new InvalidOperationException("The MainViewModel was not registered.");
        }
    }

    private static SplatServiceProvider CreateServiceProvider()
    {
        var service = new GreetingService();
        Locator.CurrentMutable.RegisterConstant<IGreetingService>(service);
        Locator.CurrentMutable.RegisterConstant(new MainViewModel(service));
        return new SplatServiceProvider();
    }
#else
    private static readonly Lazy<MainViewModel> s_mainViewModel = new(
        () => new MainViewModel(new GreetingService()));

    public static MainViewModel MainViewModel => s_mainViewModel.Value;
#endif
}
