namespace Akbura.Akcss;

/// <summary>
/// Represents one ordered application of an AKCSS style to a target.
/// </summary>
public abstract class AkcssStyleActivator
{
    protected AkcssStyleActivator(AkcssStyle style)
    {
        Style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>
    /// Gets the style applied by this activator.
    /// </summary>
    public AkcssStyle Style { get; }

    /// <summary>
    /// Gets whether this application has a runtime condition.
    /// </summary>
    public virtual bool IsConditional => false;

    /// <summary>
    /// Gets whether the style should be active in the current pass.
    /// </summary>
    public virtual bool Condition => true;

    /// <summary>
    /// Applies the style to the target.
    /// </summary>
    public abstract void Execute(object target);

    /// <summary>
    /// Removes values previously written by this style.
    /// </summary>
    public virtual void Reset(object target)
    {
        Style.Reset(target);
    }

    /// <summary>
    /// Returns a signal that requires the complete AKCSS cascade to run again.
    /// </summary>
    public virtual IObservable<object?> Watch(object target)
    {
        return Style.Watch(target);
    }
}
