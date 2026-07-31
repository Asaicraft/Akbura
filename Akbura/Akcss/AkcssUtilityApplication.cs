using Avalonia.Controls;
using System.ComponentModel;

namespace Akbura.Akcss;

/// <summary>
/// Connects one target-specific utility implementation to a generated candidate.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkcssUtilityApplication
{
    private readonly Action<Control, IReadOnlyList<object?>> _execute;

    public AkcssUtilityApplication(
        AkcssUtility utility,
        Action<Control, IReadOnlyList<object?>> execute)
    {
        Utility = utility ??
            throw new ArgumentNullException(nameof(utility));
        _execute = execute ??
            throw new ArgumentNullException(nameof(execute));
    }

    public AkcssUtility Utility { get; }

    internal void Execute(
        Control target,
        IReadOnlyList<object?> arguments)
    {
        _execute(target, arguments);
    }
}
