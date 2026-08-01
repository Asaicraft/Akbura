using Akbura.CompilerAnotations;
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

    public abstract bool IsActive(
        object target,
        IReadOnlyList<object?> arguments);

    public abstract void Update(
        object target,
        IReadOnlyList<object?> arguments);
}
