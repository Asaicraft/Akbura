using Akbura.HotReload;
using Avalonia.Controls;
using System.ComponentModel;

namespace Akbura.Markup;

/// <summary>Supplies the current native lexical scope without retaining an obsolete scope snapshot.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaRetainedFactoryServiceProvider : IServiceProvider
{
    private readonly AkburaRenderState _owner;
    private readonly long _scopeIdentity;
    private readonly IServiceProvider _fallback;

    internal AkburaRetainedFactoryServiceProvider(
        AkburaRenderState owner, long scopeIdentity, IServiceProvider fallback)
    {
        _owner = owner;
        _scopeIdentity = scopeIdentity;
        _fallback = fallback;
    }

    public INameScope? CurrentNameScope => _owner.GetRetainedFactoryNameScope(_scopeIdentity);

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(INameScope))
        {
            return CurrentNameScope;
        }

        if (serviceType == typeof(AkburaRetainedFactoryServiceProvider) || serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        return _fallback.GetService(serviceType);
    }
}
