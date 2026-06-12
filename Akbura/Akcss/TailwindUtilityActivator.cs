using Avalonia.Controls;
using System;
using System.Collections.Immutable;

namespace Akbura.Akcss;

public abstract class TailwindUtilityActivator
{
    protected TailwindUtilityActivator(
        AkcssUtility utility,
        bool isConditional,
        ImmutableArray<object?> arguments)
    {
        Utility = utility ?? throw new ArgumentNullException(nameof(utility));
        IsConditional = isConditional;
        Arguments = arguments.IsDefault ? ImmutableArray<object?>.Empty : arguments;
    }

    public AkcssUtility Utility { get; }

    public bool IsConditional { get; }

    public abstract bool Condition { get; }

    public ImmutableArray<object?> Arguments { get; }

    public abstract void Execute(Control control);

    public virtual IObservable<object?> Watch(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return Utility.Watch(control);
    }
}
