using System.ComponentModel;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Akbura.HotReload;

/// <summary>Applies styles invalidated by replacing an owned style subtree.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public static class AkburaRenderStyleHelper
{
    public static void ApplyStyling(object? owner)
    {
        if (owner is not StyledElement element)
        {
            return;
        }

        ApplyStyling(element, new HashSet<StyledElement>(ReferenceEqualityComparer.Instance));
    }

    private static void ApplyStyling(object? owner, HashSet<StyledElement> visited)
    {
        if (owner is not StyledElement element || !visited.Add(element))
        {
            return;
        }

        element.ApplyStyling();
        foreach (var child in ((ILogical)element).LogicalChildren)
        {
            ApplyStyling(child, visited);
        }

        // Template children can belong only to the visual tree. A shared identity
        // set keeps traversal linear when the logical and visual trees overlap.
        if (element is Visual visual)
        {
            foreach (var child in visual.GetVisualChildren())
            {
                ApplyStyling(child, visited);
            }
        }
    }
}
