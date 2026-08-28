using Avalonia;

namespace Akbura.ComponentTree;

public sealed class ReadOnlyParameter<TOwner, TValue> : Parameter<TValue>
    where TOwner : AkburaControl
{
    internal ReadOnlyParameter(DirectProperty<TOwner, TValue> avaloniaProperty)
        : base(
            avaloniaProperty,
            ParameterBinding.In,
            default,
            isAlwaysSet: true)
    {
    }

    public new DirectProperty<TOwner, TValue> AvaloniaProperty =>
        (DirectProperty<TOwner, TValue>)base.AvaloniaProperty;

    public static implicit operator DirectProperty<TOwner, TValue>(
        ReadOnlyParameter<TOwner, TValue> parameter)
    {
        return parameter.AvaloniaProperty;
    }
}
