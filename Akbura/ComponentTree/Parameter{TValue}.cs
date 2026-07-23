using Avalonia;
using Avalonia.Data;

namespace Akbura.ComponentTree;

public abstract class Parameter<TValue> : Parameter
{
    internal Parameter(
        AvaloniaProperty<TValue> avaloniaProperty,
        ParameterBinding parameterBinding,
        Optional<TValue> defaultValue,
        bool isAlwaysSet = false)
        : base(
            avaloniaProperty,
            parameterBinding,
            defaultValue.HasValue ? defaultValue.Value : default,
            defaultValue.HasValue,
            isAlwaysSet)
    {
    }

    public new AvaloniaProperty<TValue> AvaloniaProperty =>
        (AvaloniaProperty<TValue>)base.AvaloniaProperty;

    public new TValue? DefaultValue => (TValue?)base.DefaultValue;

    public static implicit operator AvaloniaProperty<TValue>(Parameter<TValue> parameter)
    {
        return parameter.AvaloniaProperty;
    }
}
