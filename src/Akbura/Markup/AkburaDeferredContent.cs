using Avalonia.Controls;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Markup;

/// <summary>
/// Adapts an Akbura-generated delegate factory
/// to Avalonia's IDeferredContent contract.
/// </summary>
internal sealed class AkburaDeferredContent<T> :
    IDeferredContent
{
    private readonly Func<IServiceProvider, object> _build;

    public AkburaDeferredContent(
        Func<IServiceProvider, object> builder,
        IServiceProvider parentServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(
            parentServiceProvider);

        _build =
            XamlIlRuntimeHelpers
                .DeferredTransformationFactoryV2<T>(
                    builder,
                    parentServiceProvider);
    }

    public object? Build(
        IServiceProvider? serviceProvider)
    {
        // Null is valid for Avalonia TemplateContent.Load.
        // Nullable annotations do not change the runtime
        // delegate signature.
        return _build(serviceProvider!);
    }
}