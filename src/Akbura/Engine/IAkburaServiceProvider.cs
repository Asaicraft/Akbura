using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Engine;

public interface IAkburaServiceProvider : IServiceProvider
{
    object? IServiceProvider.GetService(Type serviceType)
    {
        var injectInfo = new InjectionInfo(serviceType);

        return GetService(in injectInfo);
    }

    public object? GetService(ref readonly InjectionInfo injectionInfo);
}
