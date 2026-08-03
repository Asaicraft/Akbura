using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Akbura.ComponentTree;

internal static class AkburaComponentRegistry
{
    private static readonly object s_gate = new();
    private static readonly List<WeakReference<AkburaControl>> s_components = [];
    private static readonly ConditionalWeakTable<TopLevel, object> s_excludedTopLevels = new();

    internal static event EventHandler? Changed;

    internal static void ExcludeTopLevel(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        s_excludedTopLevels.GetValue(topLevel, static _ => new object());
    }

    internal static ImmutableArray<AkburaControl> GetAttachedComponents()
    {
        lock (s_gate)
        {
            var builder = ImmutableArray.CreateBuilder<AkburaControl>(s_components.Count);
            for (var index = 0; index < s_components.Count;)
            {
                if (s_components[index].TryGetTarget(out var component))
                {
                    builder.Add(component);
                    index++;
                }
                else
                {
                    s_components.RemoveAt(index);
                }
            }

            return builder.ToImmutable();
        }
    }

    internal static void Attach(AkburaControl component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (TopLevel.GetTopLevel(component) is { } topLevel &&
            s_excludedTopLevels.TryGetValue(topLevel, out _))
        {
            return;
        }

        lock (s_gate)
        {
            for (var index = s_components.Count - 1; index >= 0; index--)
            {
                if (!s_components[index].TryGetTarget(out var existing))
                {
                    s_components.RemoveAt(index);
                    continue;
                }

                if (ReferenceEquals(existing, component))
                {
                    return;
                }
            }

            s_components.Add(new WeakReference<AkburaControl>(component));
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static void Detach(AkburaControl component)
    {
        ArgumentNullException.ThrowIfNull(component);

        var changed = false;
        lock (s_gate)
        {
            for (var index = s_components.Count - 1; index >= 0; index--)
            {
                if (!s_components[index].TryGetTarget(out var existing) ||
                    ReferenceEquals(existing, component))
                {
                    s_components.RemoveAt(index);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
