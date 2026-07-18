using Avalonia;
using Avalonia.Data;

namespace Akbura.ComponentTree;

public sealed class Parameter<TOwner, TValue> : Parameter<TValue>
    where TOwner : AkburaControl
{
    internal Parameter(
        StyledProperty<TValue> avaloniaProperty,
        ParameterBinding parameterBinding,
        Optional<TValue> defaultValue)
        : base(avaloniaProperty, parameterBinding, defaultValue)
    {
    }

    public new StyledProperty<TValue> AvaloniaProperty =>
        (StyledProperty<TValue>)base.AvaloniaProperty;

    public static implicit operator StyledProperty<TValue>(
        Parameter<TOwner, TValue> parameter)
    {
        return parameter.AvaloniaProperty;
    }
}
