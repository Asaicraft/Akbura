using System;

namespace Akbura.Engine;

internal sealed class LinkedAkburaServiceProvider : IAkburaServiceProvider
{
    private static readonly LinkedAkburaServiceProvider s_empty = new(null, null);

    private readonly IAkburaServiceProvider? _serviceProvider;
    private readonly NextProvider? _nextProvider;

    private LinkedAkburaServiceProvider(
        IAkburaServiceProvider? serviceProvider,
        NextProvider? nextProvider)
    {
        _serviceProvider = serviceProvider;
        _nextProvider = nextProvider;
    }

    public static LinkedAkburaServiceProvider Build(
        ReadOnlySpan<IAkburaServiceProvider> serviceProviders)
    {
        if (serviceProviders.IsEmpty)
        {
            return s_empty;
        }

        for (var index = 0; index < serviceProviders.Length; index++)
        {
            if (serviceProviders[index] is null)
            {
                throw new ArgumentException(
                    $"Service provider at index {index} is null.",
                    nameof(serviceProviders));
            }
        }

        NextProvider? nextProvider = null;
        for (var index = serviceProviders.Length - 1; index > 0; index--)
        {
            nextProvider = new NextProvider(serviceProviders[index], nextProvider);
        }

        return new LinkedAkburaServiceProvider(serviceProviders[0], nextProvider);
    }

    public object? GetService(ref readonly InjectionInfo injectionInfo)
    {
        return Invoke(_serviceProvider, _nextProvider, in injectionInfo);
    }

    private static object? Invoke(
        IAkburaServiceProvider? serviceProvider,
        IAkburaServiceProvider? nextProvider,
        ref readonly InjectionInfo injectionInfo)
    {
        if (serviceProvider is null)
        {
            return null;
        }

        var contextualInjectionInfo = injectionInfo with
        {
            NextProvider = nextProvider,
        };
        return serviceProvider.GetService(in contextualInjectionInfo);
    }

    private sealed class NextProvider : IAkburaServiceProvider
    {
        private readonly IAkburaServiceProvider _serviceProvider;
        private readonly NextProvider? _nextProvider;

        public NextProvider(
            IAkburaServiceProvider serviceProvider,
            NextProvider? nextProvider)
        {
            _serviceProvider = serviceProvider;
            _nextProvider = nextProvider;
        }

        public object? GetService(ref readonly InjectionInfo injectionInfo)
        {
            return Invoke(_serviceProvider, _nextProvider, in injectionInfo);
        }
    }
}
