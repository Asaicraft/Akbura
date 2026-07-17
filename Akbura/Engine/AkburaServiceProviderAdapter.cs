using System;

namespace Akbura.Engine;

public sealed class AkburaServiceProviderAdapter : IAkburaServiceProvider
{
    private readonly IServiceProvider _serviceProvider;

    public AkburaServiceProviderAdapter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ??
            throw new ArgumentNullException(nameof(serviceProvider));
    }

    object? IServiceProvider.GetService(Type type)
    {
        return _serviceProvider.GetService(type);
    }

    public object? GetService(ref readonly InjectionInfo injectionInfo)
    {
        var service = _serviceProvider.GetService(injectionInfo.RequestedService);
        if (service != null)
        {
            return service;
        }

        var nextProvider = injectionInfo.NextProvider;
        return nextProvider == null
            ? null
            : nextProvider.GetService(in injectionInfo);
    }
}
