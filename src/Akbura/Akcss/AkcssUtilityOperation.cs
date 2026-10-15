using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Data;
using System.ComponentModel;

namespace Akbura.Akcss;

/// <summary>
/// Represents one independently resolved property write of an AKCSS utility.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public abstract class AkcssUtilityOperation : AkcssStyle
{
    protected AkcssUtilityOperation(
        AkcssUtility utility,
        string conflictKey,
        AkcssOperationPriority priority,
        int order)
    {
        Utility = utility ??
            throw new ArgumentNullException(nameof(utility));

        if (string.IsNullOrWhiteSpace(conflictKey))
        {
            throw new ArgumentException(
                "An AKCSS utility operation conflict key is required.",
                nameof(conflictKey));
        }

        ConflictKey = conflictKey;
        Priority = priority;
        Order = order;
        NameCore = utility.Name;
        IsInlinedCore = utility.IsInlined;
    }

    public AkcssUtility Utility { get; }

    public string ConflictKey { get; }

    public AkcssOperationPriority Priority { get; }

    public int Order { get; }

    /// <summary>
    /// Gets whether this generated operation can reset one known Avalonia property.
    /// Custom and legacy operations conservatively opt out of incremental updates.
    /// </summary>
    public virtual bool SupportsHotReloadReconciliation => false;

    /// <summary>
    /// Gets whether Apply returns an independently suspendable property contribution.
    /// Ordinary LocalValue assignments and direct properties remain compatibility paths.
    /// </summary>
    public virtual bool SupportsOwnedContributions => false;

    /// <summary>
    /// Opts into scalar publication without resetting the owned binding first.
    /// Operations with target-dependent eligibility or arbitrary effects must opt out.
    /// </summary>
    public virtual bool SupportsContributionUpdates => false;

    /// <summary>
    /// Updates an existing owned scalar source, or returns false without effects.
    /// </summary>
    public virtual bool TryUpdateContribution(object target, IReadOnlyList<object?> arguments,
        BindingPriority priority, IDisposable contribution) => false;

    /// <summary>
    /// Gets the Avalonia property captured when a Debug candidate is constructed.
    /// </summary>
    public virtual AvaloniaProperty? HotReloadProperty => null;

    /// <summary>
    /// Gets the receiver guard captured alongside the original reset property.
    /// </summary>
    public virtual Type HotReloadResetTargetType => typeof(AvaloniaObject);

    /// <summary>
    /// Clears a captured property without relying on a replaced generated Reset body.
    /// </summary>
    public virtual void Reset(object target, AvaloniaProperty property)
    {
        if (target is AvaloniaObject avaloniaObject)
        {
            avaloniaObject.ClearValue(property);
        }
    }

    public abstract bool IsActive(
        object target,
        IReadOnlyList<object?> arguments);

    public abstract void Update(
        object target,
        IReadOnlyList<object?> arguments);

    /// <summary>
    /// Applies this operation at a reversible Avalonia binding layer.
    /// </summary>
    public virtual IDisposable Apply(
        object target,
        IReadOnlyList<object?> arguments,
        BindingPriority priority)
    {
        throw new NotSupportedException(
            $"AKCSS utility operation '{GetType()}' does not support binding priorities.");
    }
}
