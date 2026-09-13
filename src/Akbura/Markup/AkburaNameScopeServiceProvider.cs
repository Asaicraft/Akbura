using Avalonia.Controls;
using System.ComponentModel;

namespace Akbura.Markup;

/// <summary>Preserves the native XAML services while supplying a lexical name scope.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaNameScopeServiceProvider(INameScope nameScope, IServiceProvider fallback) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(INameScope))
        {
            return nameScope;
        }
        return serviceType == typeof(IServiceProvider) ? this : fallback.GetService(serviceType);
    }
}
