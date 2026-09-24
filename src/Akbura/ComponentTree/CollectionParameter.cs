using Avalonia;
using Avalonia.Data;
using System.ComponentModel;

namespace Akbura.ComponentTree;

/// <summary>A writable direct parameter whose getter exposes a per-owner collection.</summary>
public sealed class CollectionParameter<TOwner, TValue> : Parameter<TValue>
    where TOwner : AkburaControl
{
    private readonly Action<TOwner, object?> _setSource;
    private readonly Action<TOwner> _refreshSource;

    internal CollectionParameter(
        DirectProperty<TOwner, TValue> property,
        DirectProperty<TOwner, object?> sourceProperty,
        Action<TOwner, object?> setSource,
        Action<TOwner> refreshSource)
        : base(property, ParameterBinding.In, default, isAlwaysSet: true)
    {
        SourceProperty = sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty));
        _setSource = setSource ?? throw new ArgumentNullException(nameof(setSource));
        _refreshSource = refreshSource ?? throw new ArgumentNullException(nameof(refreshSource));
    }

    public new DirectProperty<TOwner, TValue> AvaloniaProperty =>
        (DirectProperty<TOwner, TValue>)base.AvaloniaProperty;

    /// <summary>The untyped input used to preserve the identity of an external collection source.</summary>
    public DirectProperty<TOwner, object?> SourceProperty { get; }

    /// <summary>Assigns an external collection source without weakening the typed parameter property.</summary>
    public void SetSource(TOwner owner, object? source)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (ReferenceEquals(source, BindingOperations.DoNothing)) return;
        if (ReferenceEquals(source, global::Avalonia.AvaloniaProperty.UnsetValue)) source = null;
        _setSource(owner, source);
    }

    /// <summary>Refreshes the snapshot of the currently assigned non-notifying source.</summary>
    public void RefreshSource(TOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _refreshSource(owner);
    }

    public static implicit operator DirectProperty<TOwner, TValue>(CollectionParameter<TOwner, TValue> parameter) =>
        parameter.AvaloniaProperty;
}
