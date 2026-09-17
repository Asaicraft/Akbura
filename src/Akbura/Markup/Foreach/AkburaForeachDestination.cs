using System.Collections;
using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>Validates an indexed destination before a loop enumerates or constructs children.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaForeachDestination
{
    public static void Validate<TChild>(IList<TChild> target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsReadOnly || target is IList { IsFixedSize: true })
        {
            throw new InvalidOperationException("A foreach destination must be mutable and support indexed insertion and removal.");
        }
    }

    public static void Validate<TChild>(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is IList<TChild> generic)
        {
            Validate(generic);
            return;
        }

        if (target is not IList indexed || indexed.IsReadOnly || indexed.IsFixedSize)
        {
            throw new InvalidOperationException("A foreach destination must be mutable and support indexed insertion and removal.");
        }
    }
}
