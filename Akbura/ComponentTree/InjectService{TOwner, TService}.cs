using Akbura.Engine;
using Avalonia;

namespace Akbura.ComponentTree;

public sealed class InjectService<TOwner, TService> : InjectService<TService>
    where TOwner : AkburaControl
    where TService : class
{
    internal InjectService(
        DirectProperty<TOwner, TService?> avaloniaProperty,
        bool isOptional)
        : base(avaloniaProperty, isOptional)
    {
    }

    public new DirectProperty<TOwner, TService?> AvaloniaProperty =>
        (DirectProperty<TOwner, TService?>)base.AvaloniaProperty;

    public override bool IsInjected(AkburaControl control)
    {
        return AvaloniaProperty.Getter(GetOwner(control)) != null;
    }

    public override void Inject(AkburaControl control, AkburaEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var owner = GetOwner(control);
        if (AvaloniaProperty.Getter(owner) != null)
        {
            return;
        }

        var service = engine.GetService(
            owner,
            typeof(TService),
            IsOptional,
            Name);
        if (service == null)
        {
            if (IsOptional)
            {
                return;
            }

            throw new AkburaServiceNotFoundException(owner, this);
        }

        AvaloniaProperty.Setter!(owner, (TService)service);
    }

    public static implicit operator DirectProperty<TOwner, TService?>(
        InjectService<TOwner, TService> service)
    {
        return service.AvaloniaProperty;
    }

    private static TOwner GetOwner(AkburaControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control is TOwner owner)
        {
            return owner;
        }

        throw new ArgumentException(
            $"Service '{typeof(TService).FullName}' belongs to " +
            $"'{typeof(TOwner).FullName}', not '{control.GetType().FullName}'.",
            nameof(control));
    }
}
