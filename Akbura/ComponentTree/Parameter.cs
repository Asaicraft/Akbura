using Avalonia;
using Avalonia.Data;

namespace Akbura.ComponentTree;

public abstract class Parameter
{
    private readonly object? _defaultValue;
    private readonly bool _hasDefaultValue;

    internal Parameter(
        AvaloniaProperty avaloniaProperty,
        ParameterBinding binding,
        object? defaultValue,
        bool hasDefaultValue)
    {
        Binding = binding;
        AvaloniaProperty = avaloniaProperty;
        _defaultValue = defaultValue;
        _hasDefaultValue = hasDefaultValue;
    }

    public string Name => AvaloniaProperty.Name;

    public ParameterBinding Binding
    {
        get;
    }

    public AvaloniaProperty AvaloniaProperty
    {
        get;
    }

    public object? DefaultValue => _defaultValue;

    public bool HasDefaultValue => _hasDefaultValue;

    /// <summary>
    /// Determines whether this parameter has a value suitable for component initialization.
    /// </summary>
    /// <param name="control">The component that owns the parameter.</param>
    /// <returns>
    /// <see langword="true"/> when the parameter is output-only, has a default value,
    /// or its styled property has been set; otherwise, <see langword="false"/>.
    /// </returns>
    public bool IsSet(AkburaControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return Binding == ParameterBinding.Out ||
            HasDefaultValue ||
            control.IsSet(AvaloniaProperty);
    }

    public static implicit operator AvaloniaProperty(Parameter parameter)
    {
        return parameter.AvaloniaProperty;
    }

    /// <summary>
    /// Creates a component parameter backed by an Avalonia styled property.
    /// </summary>
    /// <typeparam name="TOwner">The component type that declares the parameter.</typeparam>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter and styled property name.</param>
    /// <param name="defaultValue">The optional default value.</param>
    /// <param name="parameterBinding">The direction in which the parameter is bound.</param>
    /// <returns>The parameter descriptor. The caller should cache this instance.</returns>
    public static Parameter<TOwner, TValue> Create<TOwner, TValue>(
        string name,
        Optional<TValue> defaultValue = default,
        ParameterBinding parameterBinding = ParameterBinding.In)
        where TOwner : AkburaControl
    {
        var bindingMode = parameterBinding.ToBindingMode();
#pragma warning disable AVP1001 // The same AvaloniaProperty should not be registered twice
        var property = AvaloniaProperty.Register<TOwner, TValue>(
            name,
            defaultValue: defaultValue.HasValue ? defaultValue.Value : default!,
            defaultBindingMode: bindingMode);
#pragma warning restore AVP1001 // The same AvaloniaProperty should not be registered twice

        property.Changed.AddClassHandler<TOwner>(
            static (owner, _) => owner.OnParameterChanged());

        return new(property, parameterBinding, defaultValue);
    }
}
