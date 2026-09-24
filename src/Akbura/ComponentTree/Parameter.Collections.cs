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
        return CreateCollection(
            name,
            getter,
            setter,
            owner => getter(owner),
            (owner, source) => setter(owner, CastCollectionSource<TValue>(source)),
            static _ => { });
    }

    /// <summary>
    /// Creates a typed collection parameter with a separate untyped source input.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static CollectionParameter<TOwner, TValue> CreateCollection<TOwner, TValue>(
        string name,
        Func<TOwner, TValue> getter,
        Action<TOwner, TValue> setter,
        Func<TOwner, object?> sourceGetter,
        Action<TOwner, object?> sourceSetter,
        Action<TOwner> sourceRefresher)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ArgumentNullException.ThrowIfNull(sourceGetter);
        ArgumentNullException.ThrowIfNull(sourceSetter);
        ArgumentNullException.ThrowIfNull(sourceRefresher);
#pragma warning disable AVP1001
        var property = AvaloniaProperty.RegisterDirect<TOwner, TValue>(name, getter, setter,
            defaultBindingMode: BindingMode.OneWay);
        var sourceProperty = AvaloniaProperty.RegisterDirect<TOwner, object?>(
            "__AkburaCollectionSource_" + name,
            sourceGetter,
            sourceSetter,
            defaultBindingMode: BindingMode.OneWay);
#pragma warning restore AVP1001
        return new CollectionParameter<TOwner, TValue>(property, sourceProperty, sourceSetter, sourceRefresher);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static CollectionParameter<TOwner, TValue> RecreateCollectionForHotReload<TOwner, TValue>(
        DirectProperty<TOwner, TValue>? property, string name,
        Func<TOwner, TValue> getter, Action<TOwner, TValue> setter)
        where TOwner : AkburaControl
    {
        return RecreateCollectionForHotReload(
            property,
            sourceProperty: null,
            name,
            getter,
            setter,
            owner => getter(owner),
            (owner, source) => setter(owner, CastCollectionSource<TValue>(source)),
            static _ => { });
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static CollectionParameter<TOwner, TValue> RecreateCollectionForHotReload<TOwner, TValue>(
        DirectProperty<TOwner, TValue>? property,
        DirectProperty<TOwner, object?>? sourceProperty,
        string name,
        Func<TOwner, TValue> getter,
        Action<TOwner, TValue> setter,
        Func<TOwner, object?> sourceGetter,
        Action<TOwner, object?> sourceSetter,
        Action<TOwner> sourceRefresher)
        where TOwner : AkburaControl
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ArgumentNullException.ThrowIfNull(sourceGetter);
        ArgumentNullException.ThrowIfNull(sourceSetter);
        ArgumentNullException.ThrowIfNull(sourceRefresher);
        if (property == null)
        {
            return CreateCollection(name, getter, setter, sourceGetter, sourceSetter, sourceRefresher);
        }
        if (property.Name != name || property.IsReadOnly)
        {
            throw new ArgumentException("A collection parameter needs a compatible writable direct property.", nameof(property));
        }
        var sourceName = "__AkburaCollectionSource_" + name;
        if (sourceProperty == null)
        {
#pragma warning disable AVP1001
            sourceProperty = AvaloniaProperty.RegisterDirect<TOwner, object?>(
                sourceName,
                sourceGetter,
                sourceSetter,
                defaultBindingMode: BindingMode.OneWay);
#pragma warning restore AVP1001
        }
        else if (sourceProperty.Name != sourceName || sourceProperty.IsReadOnly)
        {
            throw new ArgumentException("A collection parameter needs a compatible writable source property.", nameof(sourceProperty));
        }
        return new CollectionParameter<TOwner, TValue>(property, sourceProperty, sourceSetter, sourceRefresher);
    }

    private static TValue CastCollectionSource<TValue>(object? source)
    {
        if (source is TValue value) return value;
        if (source == null && default(TValue) is null) return default!;
        throw new ArgumentException(
            $"Collection source '{source?.GetType().FullName ?? "null"}' is not compatible with '{typeof(TValue)}'.",
            nameof(source));
    }
}
