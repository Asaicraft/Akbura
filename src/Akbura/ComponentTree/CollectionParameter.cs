using Avalonia;
using Avalonia.Data;
using System.ComponentModel;

namespace Akbura.ComponentTree;

/// <summary>A writable direct parameter whose getter exposes a per-owner collection.</summary>
public sealed class CollectionParameter<TOwner, TValue> : Parameter<TValue>
    where TOwner : AkburaControl
{
    internal CollectionParameter(DirectProperty<TOwner, TValue> property)
        : base(property, ParameterBinding.In, default, isAlwaysSet: true) { }

    public new DirectProperty<TOwner, TValue> AvaloniaProperty =>
        (DirectProperty<TOwner, TValue>)base.AvaloniaProperty;

    public static implicit operator DirectProperty<TOwner, TValue>(CollectionParameter<TOwner, TValue> parameter) =>
        parameter.AvaloniaProperty;
}
