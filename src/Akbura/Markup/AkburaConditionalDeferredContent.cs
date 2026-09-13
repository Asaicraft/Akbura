using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using System.ComponentModel;

namespace Akbura.Markup;

/// <summary>Preserves native deferred services without freezing a conditional factory's parent name scope.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaConditionalDeferredContent<T> : IDeferredContent where T : Control
{
    private readonly Func<IServiceProvider, object> _build;
    private readonly AkburaRetainedFactoryServiceProvider _parentServices;

    public AkburaConditionalDeferredContent(
        Func<IServiceProvider, object> builder,
        AkburaRetainedFactoryServiceProvider parentServices)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(parentServices);

        _parentServices = parentServices;
        // V2 snapshots INameScope at construction. Its native per-build scope
        // must not have a strong parent reference to an obsolete lexical scope.
        _build = XamlIlRuntimeHelpers.DeferredTransformationFactoryV2<T>(
            builder, new DeclaringServices(parentServices));
    }

    public object? Build(IServiceProvider? serviceProvider)
    {
        // The SDK forwards build services, rather than declaring services, to
        // the generated builder. Preserve the retained-factory marker there.
        var result = (TemplateResult<T>)_build(new BuildServices(_parentServices, serviceProvider));
        if (result.Result is { } root && NameScope.GetNameScope(root) is { } scope)
        {
            return new TemplateResult<T>(root, scope);
        }

        return result;
    }

    private sealed class DeclaringServices(AkburaRetainedFactoryServiceProvider parent) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(INameScope) ? null : parent.GetService(serviceType);
        }
    }

    private sealed class BuildServices(
        AkburaRetainedFactoryServiceProvider parent, IServiceProvider? services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(AkburaRetainedFactoryServiceProvider))
            {
                return parent;
            }

            return services?.GetService(serviceType) ?? parent.GetService(serviceType);
        }
    }
}
