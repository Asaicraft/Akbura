using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Engine;

internal sealed class EmptyServiceProvider : IAkburaServiceProvider
{
    public static readonly EmptyServiceProvider Instance = new();

    public object? GetService(ref readonly InjectionInfo injectionInfo)
    {
        return null;
    }
}
