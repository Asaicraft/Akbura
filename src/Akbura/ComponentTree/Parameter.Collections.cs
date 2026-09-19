using Avalonia;
using Avalonia.Data;
using System.ComponentModel;

namespace Akbura.ComponentTree;

public abstract partial class Parameter
{
    /// <summary>
    /// The setter accepts the source; the getter returns the owned collection.
    /// Reference binding defaults to OneWay. Item synchronization is independent.
    /// </summary>
    public static CollectionParameter<TOwner, TValue> CreateCollection<TOwner, TValue>(
        string name, Func<TOwner, TValue> getter, Action<TOwner, TValue> setter)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
#pragma warning disable AVP1001
        var property = AvaloniaProperty.RegisterDirect<TOwner, TValue>(name, getter, setter,
            defaultBindingMode: BindingMode.OneWay);
#pragma warning restore AVP1001
        return new CollectionParameter<TOwner, TValue>(property);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static CollectionParameter<TOwner, TValue> RecreateCollectionForHotReload<TOwner, TValue>(
        DirectProperty<TOwner, TValue>? property, string name,
        Func<TOwner, TValue> getter, Action<TOwner, TValue> setter)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        if (property == null) return CreateCollection(name, getter, setter);
        if (property.Name != name || property.IsReadOnly)
            throw new ArgumentException("A collection parameter needs a compatible writable direct property.", nameof(property));
        return new CollectionParameter<TOwner, TValue>(property);
    }
}
