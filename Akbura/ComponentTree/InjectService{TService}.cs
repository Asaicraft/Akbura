using Avalonia;

namespace Akbura.ComponentTree;

public abstract class InjectService<TService> : InjectService
    where TService : class
{
    internal InjectService(
        AvaloniaProperty<TService?> avaloniaProperty,
        bool isOptional)
        : base(avaloniaProperty, typeof(TService), isOptional)
    {
    }

    public new AvaloniaProperty<TService?> AvaloniaProperty =>
        (AvaloniaProperty<TService?>)base.AvaloniaProperty;

    public static implicit operator AvaloniaProperty<TService?>(
        InjectService<TService> service)
    {
        return service.AvaloniaProperty;
    }
}
