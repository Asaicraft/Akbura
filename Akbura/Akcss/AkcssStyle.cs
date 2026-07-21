using Akbura.CompilerAnotations;
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
    /// Removes values previously written by this style.
    /// </summary>
    /// <remarks>
    /// Generated styles override this method and clear their values in reverse cascade order.
    /// Hand-written interceptors only need to override it when they retain values between updates.
    /// </remarks>
    public virtual void Reset(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
    }

    /// <summary>
    /// Returns a signal that this style should be applied again.
    /// </summary>
    /// <remarks>
    /// The default implementation observes properties declared with
    /// <see cref="ObservesPropertyAttribute"/>. Emitted values are signals only.
    /// </remarks>
    public virtual IObservable<object?> Watch(object target)
    {
        return AkcssObservedPropertySignal.Create(GetType(), target);
    }
}