using Akbura.Markup;
using System.Reflection;

namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    private static void SetClrRenderPropertyValue(object target, PropertyInfo property, object? value)
    {
        property.SetValue(target, value);
        if (property.Name == "Content" &&
            (property.DeclaringType == typeof(Avalonia.Markup.Xaml.Templates.DataTemplate) ||
             property.DeclaringType == typeof(Avalonia.Markup.Xaml.Templates.ControlTemplate)))
        {
            // These native deferred-content properties have no change event.
            // Apply, baseline restoration and rollback must notify the same hosts.
            AkburaConditionalDeferredTemplate.NotifyTemplateContentChanged(target);
        }
    }
}
