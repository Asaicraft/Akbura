using Akbura.Engine;
using Avalonia;

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
}
