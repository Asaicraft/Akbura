using Avalonia.Controls;

namespace Akbura.Akcss;

/// <summary>
/// Applies an AKCSS utility through code supplied by a generated component.
/// </summary>
public sealed class AkcssUtilityActivator : AkcssStyleActivator
{
    private readonly Action<Control> _execute;
    private readonly Func<bool>? _condition;

    public AkcssUtilityActivator(
        AkcssUtility utility,
        Action<Control> execute,
        Func<bool>? condition = null)
        : base(utility)
    {
        Utility = utility;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _condition = condition;
    }

    /// <summary>
    /// Gets the utility represented by this application.
    /// </summary>
    public AkcssUtility Utility { get; }

    public override bool IsConditional => _condition != null;

    public override bool Condition => _condition?.Invoke() ?? true;

    public override void Execute(object target)
    {
        _execute(GetControl(target));
    }

    public override void Reset(object target)
    {
        Utility.Reset(GetControl(target));
    }

    public override IObservable<object?> Watch(object target)
    {
        return Utility.Watch(GetControl(target));
    }

    private static Control GetControl(object target)
    {
        return target as Control ?? throw new ArgumentException(
            $"An AKCSS utility target must derive from '{typeof(Control)}'.",
            nameof(target));
    }
}
