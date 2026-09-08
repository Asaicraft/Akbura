using Avalonia;
using Avalonia.Data;
using System.ComponentModel;

namespace Akbura.ComponentTree;

public abstract class Parameter
{
    private readonly object? _defaultValue;
    private readonly bool _hasDefaultValue;
    private readonly bool _isAlwaysSet;

    internal Parameter(
        AvaloniaProperty avaloniaProperty,
        ParameterBinding binding,
        object? defaultValue,
        bool hasDefaultValue,
        bool isAlwaysSet)
    {
        Binding = binding;
        AvaloniaProperty = avaloniaProperty;
        _defaultValue = defaultValue;
        _hasDefaultValue = hasDefaultValue;
        _isAlwaysSet = isAlwaysSet;
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
            _isAlwaysSet ||
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
    /// <param name="changed">
    /// An optional callback invoked before the component update is requested.
    /// </param>
    /// <returns>The parameter descriptor. The caller should cache this instance.</returns>
    public static Parameter<TOwner, TValue> Create<TOwner, TValue>(
        string name,
        Optional<TValue> defaultValue = default,
        ParameterBinding parameterBinding = ParameterBinding.In,
        Action<TOwner, AvaloniaPropertyChangedEventArgs>? changed = null)
        where TOwner : AkburaControl
    {
        var bindingMode = parameterBinding.ToBindingMode();
#pragma warning disable AVP1001 // The same AvaloniaProperty should not be registered twice
        var property = AvaloniaProperty.Register<TOwner, TValue>(
            name,
            defaultValue: defaultValue.HasValue ? defaultValue.Value : default!,
            defaultBindingMode: bindingMode);
#pragma warning restore AVP1001 // The same AvaloniaProperty should not be registered twice

        property.Changed.AddClassHandler<TOwner>((owner, args) =>
        {
            changed?.Invoke(owner, args);
            owner.OnParameterChanged();
        });

        return new(property, parameterBinding, defaultValue);
    }

    /// <summary>
    /// Recreates a component parameter for Hot Reload while preserving a compatible
    /// Avalonia property registration.
    /// </summary>
    /// <typeparam name="TOwner">The component type that declares the parameter.</typeparam>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="property">
    /// The compatible styled property from the previous generated descriptor manifest,
    /// or <see langword="null"/> when a new property must be registered.
    /// </param>
    /// <param name="name">The parameter and styled property name.</param>
    /// <param name="defaultValue">The current optional default value.</param>
    /// <param name="parameterBinding">The current parameter binding direction.</param>
    /// <param name="changed">
    /// An optional callback used when a new property is registered. A reused property
    /// retains its existing class handler.
    /// </param>
    /// <returns>A new parameter descriptor around the compatible property.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static Parameter<TOwner, TValue> RecreateForHotReload<TOwner, TValue>(
        StyledProperty<TValue>? property,
        string name,
        Optional<TValue> defaultValue = default,
        ParameterBinding parameterBinding = ParameterBinding.In,
        Action<TOwner, AvaloniaPropertyChangedEventArgs>? changed = null)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (property == null)
        {
            return Create(
                name,
                defaultValue,
                parameterBinding,
                changed);
        }

        if (!string.Equals(property.Name, name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Property '{property.Name}' cannot be reused for parameter '{name}'.",
                nameof(property));
        }

        var bindingMode = parameterBinding.ToBindingMode();

        property.Unregister(typeof(TOwner));
        property.OverrideMetadata<TOwner>(
            new StyledPropertyMetadata<TValue>(
                defaultValue.HasValue
                    ? defaultValue.Value
                    : default!,
                defaultBindingMode: bindingMode));

        // The class handler belongs to the property identity. Registering it again
        // would invoke both the old and new callbacks after every value change.
        return new(property, parameterBinding, defaultValue);
    }

    /// <summary>
    /// Creates a readonly component parameter backed by an Avalonia direct property.
    /// </summary>
    /// <typeparam name="TOwner">The component type that declares the parameter.</typeparam>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter and direct property name.</param>
    /// <param name="getter">Reads the per-component value.</param>
    /// <returns>The readonly parameter descriptor. The caller should cache this instance.</returns>
    public static ReadOnlyParameter<TOwner, TValue> CreateReadOnly<TOwner, TValue>(
        string name,
        Func<TOwner, TValue> getter)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);

#pragma warning disable AVP1001 // The same AvaloniaProperty should not be registered twice
        var property = AvaloniaProperty.RegisterDirect<TOwner, TValue>(
            name,
            getter);
#pragma warning restore AVP1001 // The same AvaloniaProperty should not be registered twice

        return new ReadOnlyParameter<TOwner, TValue>(property);
    }

    /// <summary>
    /// Recreates a readonly component parameter for Hot Reload while preserving a
    /// compatible Avalonia direct property registration.
    /// </summary>
    /// <typeparam name="TOwner">The component type that declares the parameter.</typeparam>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="property">
    /// The compatible direct property from the previous generated descriptor manifest,
    /// or <see langword="null"/> when a new property must be registered.
    /// </param>
    /// <param name="name">The parameter and direct property name.</param>
    /// <param name="getter">Reads the per-component value when registering a new property.</param>
    /// <returns>A new readonly parameter descriptor around the compatible property.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static ReadOnlyParameter<TOwner, TValue> RecreateReadOnlyForHotReload<TOwner, TValue>(
        DirectProperty<TOwner, TValue>? property,
        string name,
        Func<TOwner, TValue> getter)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);

        if (property == null)
        {
            return CreateReadOnly(name, getter);
        }

        if (!string.Equals(property.Name, name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Property '{property.Name}' cannot be reused for parameter '{name}'.",
                nameof(property));
        }

        return new ReadOnlyParameter<TOwner, TValue>(property);
    }
}
