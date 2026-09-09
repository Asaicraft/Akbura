using Akbura.RuntimePools;
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

        var changed = false;
        lock (s_gate)
        {
            s_excludedTopLevels.GetValue(
                topLevel,
                static _ => new object());

            for (var index = s_components.Count - 1; index >= 0; index--)
            {
                if (!s_components[index].TryGetTarget(out var component))
                {
                    s_components.RemoveAt(index);
                    continue;
                }

                if (ReferenceEquals(
                        TopLevel.GetTopLevel(component),
                        topLevel))
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

    internal static ImmutableArray<AkburaControl> GetAttachedComponents()
    {
        lock (s_gate)
        {
            using var builder =
                ImmutableArrayBuilder<AkburaControl>.Rent(s_components.Count);
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

    internal static int GetAttachedComponentCount()
    {
        lock (s_gate)
        {
            for (var index = s_components.Count - 1;
                 index >= 0;
                 index--)
            {
                if (!s_components[index].TryGetTarget(out _))
                {
                    s_components.RemoveAt(index);
                }
            }

            return s_components.Count;
        }
    }

    internal static bool Attach(AkburaControl component)
    {
        ArgumentNullException.ThrowIfNull(component);

        lock (s_gate)
        {
            if (TopLevel.GetTopLevel(component) is { } topLevel &&
                s_excludedTopLevels.TryGetValue(topLevel, out _))
            {
                return false;
            }

            for (var index = s_components.Count - 1; index >= 0; index--)
            {
                if (!s_components[index].TryGetTarget(out var existing))
                {
                    s_components.RemoveAt(index);
                    continue;
                }

                if (ReferenceEquals(existing, component))
                {
                    return true;
                }
            }

            s_components.Add(new WeakReference<AkburaControl>(component));
        }

        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    internal static bool IsParticipating(AkburaControl component)
    {
        ArgumentNullException.ThrowIfNull(component);

        lock (s_gate)
        {
            if (TopLevel.GetTopLevel(component) is { } topLevel &&
                s_excludedTopLevels.TryGetValue(topLevel, out _))
            {
                return false;
            }

            for (var index = s_components.Count - 1; index >= 0; index--)
            {
                if (!s_components[index].TryGetTarget(out var existing))
                {
                    s_components.RemoveAt(index);
                    continue;
                }

                if (ReferenceEquals(existing, component))
                {
                    return true;
                }
            }

            return false;
        }
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
