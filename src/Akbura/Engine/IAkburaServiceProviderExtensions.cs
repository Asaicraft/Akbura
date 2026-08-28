using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Engine;

public static class IAkburaServiceProviderExtensions
{
    extension(IAkburaServiceProvider provider)
    {
        public T? GetService<T>()
        {
            return (T?)provider.GetService(typeof(T));
        }

        public T GetRequiredService<T>()
        {
            var serviceType = typeof(T);
            object? service = provider.GetService(serviceType);

            return service == null 
                ? throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.") 
                : (T)service;
        }
    }
}