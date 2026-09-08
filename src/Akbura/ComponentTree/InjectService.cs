using Akbura.Engine;
using Avalonia;
using System.ComponentModel;

namespace Akbura.ComponentTree;

public abstract class InjectService
{
    protected InjectService(
        AvaloniaProperty avaloniaProperty,
        Type serviceType,
        bool isOptional)
    {
        AvaloniaProperty = avaloniaProperty ??
            throw new ArgumentNullException(nameof(avaloniaProperty));
        ServiceType = serviceType ??
            throw new ArgumentNullException(nameof(serviceType));
        IsOptional = isOptional;
    }

    public string Name => AvaloniaProperty.Name;

    public Type ServiceType
    {
        get;
    }

    public bool IsOptional
    {
        get;
    }

    public AvaloniaProperty AvaloniaProperty
    {
        get;
    }

    public abstract bool IsInjected(AkburaControl control);

    public abstract void Inject(AkburaControl control, AkburaEngine engine);

    public static implicit operator AvaloniaProperty(InjectService service)
    {
        return service.AvaloniaProperty;
    }

    public static InjectService<TOwner, TService> Create<TOwner, TService>(
        string name,
        Func<TOwner, TService?> getter,
        Action<TOwner, TService?> setter,
        bool isOptional = false)
        where TOwner : AkburaControl
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);

#pragma warning disable AVP1001 // The same AvaloniaProperty should not be registered twice
        var property = AvaloniaProperty.RegisterDirect<TOwner, TService?>(
            name,
            getter,
            setter);
#pragma warning restore AVP1001 // The same AvaloniaProperty should not be registered twice

        return new(property, isOptional);
    }

    /// <summary>
    /// Recreates an injected-service descriptor for Hot Reload while preserving a
    /// compatible Avalonia direct property registration.
    /// </summary>
    /// <typeparam name="TOwner">The component type that declares the service.</typeparam>
    /// <typeparam name="TService">The injected service type.</typeparam>
    /// <param name="property">
    /// The compatible direct property from the previous generated descriptor manifest,
    /// or <see langword="null"/> when a new property must be registered.
    /// </param>
    /// <param name="name">The service and direct property name.</param>
    /// <param name="getter">Reads the per-component service value.</param>
    /// <param name="setter">Writes the per-component service value.</param>
    /// <param name="isOptional">Whether a missing service is allowed.</param>
    /// <returns>A new service descriptor around the compatible property.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    public static InjectService<TOwner, TService> RecreateForHotReload<TOwner, TService>(
        DirectProperty<TOwner, TService?>? property,
        string name,
        Func<TOwner, TService?> getter,
        Action<TOwner, TService?> setter,
        bool isOptional = false)
        where TOwner : AkburaControl
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);

        if (property == null)
        {
            return Create(
                name,
                getter,
                setter,
                isOptional);
        }

        if (!string.Equals(property.Name, name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Property '{property.Name}' cannot be reused for service '{name}'.",
                nameof(property));
        }

        return new InjectService<TOwner, TService>(property, isOptional);
    }
}
