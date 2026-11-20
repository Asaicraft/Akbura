using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Akbura.Markup;

/// <summary>Provides the XAML services used by generated control template factories.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AkburaControlTemplateServices
{
    public static IServiceProvider Create(object targetObject, object targetProperty, object intermediateRootObject,
        Uri baseUri, IReadOnlyList<object> directParentsStack, IServiceProvider? fallbackServiceProvider = null,
        TemplatedControl? templatedParent = null, INameScope? nameScope = null)
    {
        var parents = directParentsStack;
        if (templatedParent != null)
        {
            var stack = new object[directParentsStack.Count + 1];
            stack[0] = templatedParent;
            for (var index = 0; index < directParentsStack.Count; index++)
            {
                stack[index + 1] = directParentsStack[index];
            }
            parents = stack;
        }

        var provider = new AkburaMarkupServiceProvider(targetObject, targetProperty,
            templatedParent ?? intermediateRootObject, intermediateRootObject, baseUri, parents,
            parentProvider: fallbackServiceProvider?.GetService(typeof(IAvaloniaXamlIlEagerParentStackProvider)) as IAvaloniaXamlIlEagerParentStackProvider,
            fallbackServiceProvider: fallbackServiceProvider, isControlTemplate: templatedParent != null);
        return nameScope is null ? provider : new AkburaNameScopeServiceProvider(nameScope, provider);
    }

    public static IDeferredContent CreateDeferredContent<T>(Func<IServiceProvider, object> builder, IServiceProvider parentServiceProvider) =>
        new AkburaDeferredContent<T>(builder, parentServiceProvider);

    public static void ApplyMarkupExtensionResult(AvaloniaObject target, AvaloniaProperty property,
        object? value, bool inControlTemplate)
    {
        if (value is BindingBase binding)
        {
            target.Bind(property, binding);
            return;
        }

        target.SetValue(property, value, inControlTemplate && !property.IsDirect
            ? BindingPriority.Template
            : BindingPriority.LocalValue);
    }
}
