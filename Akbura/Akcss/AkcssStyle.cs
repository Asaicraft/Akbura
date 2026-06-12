using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Controls;
using System;

namespace Akbura.Akcss;

public abstract class AkcssStyle
{
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NameCore))
            {
                return NameCore;
            }

            var type = this.GetType();
            var attributes = type.GetCustomAttributes(typeof(StyleNameAttribute), true);
            if (attributes.Length > 0)
            {
                var styleNameAttribute = (StyleNameAttribute)attributes[0];
                return (NameCore = styleNameAttribute.Name)!;
            }

            return (NameCore = type.Name)!;
        }
    }

    protected string? NameCore { get; set; }

    public bool IsInlined
    {
        get
        {
            if (IsInlinedCore.HasValue)
            {
                return IsInlinedCore.Value;
            }

            var type = this.GetType();
            var attributes = type.GetCustomAttributes(typeof(InlinedStyleAttribute), true);

            return (IsInlinedCore = attributes.Length > 0).Value;
        }
    }

    protected bool? IsInlinedCore { get; set; }

    /// <summary>
    /// Returns a signal that this style should be applied again.
    /// Emitted values are always <see cref="AvaloniaProperty.UnsetValue"/> and should be ignored.
    /// </summary>
    public virtual IObservable<object?> Watch(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return EmptyStyleSignal.Instance;
    }

    private sealed class EmptyStyleSignal : IObservable<object?>
    {
        public static readonly EmptyStyleSignal Instance = new();

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return EmptyDisposable.Instance;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
