using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

/// <summary>
/// Publishes derived values after a successful render with changed dependencies.
/// </summary>
public static class ComputedHooks
{
    [UseHook]
    public static State<T> useComputed<T>(
        [Self] this AkburaControl control,
        Func<T> compute,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(compute);

        var snapshot = dependencies.ToArray();
        var result = control.useHookState(compute);
        var cache = control.useHookState(() => new ComputedDependencies(snapshot)).Value;

        // Keep the cache unchanged if compute throws. An aborted render never
        // executes this effect, and the initial result has already been computed.
        control.useEffect((Action)(() =>
        {
            if (cache.Matches(snapshot))
            {
                return;
            }

            var value = compute();
            cache.Values = snapshot;
            result.Value = value;
        }));

        return result;
    }

    [UseHook]
    public static State<TResult> useSelect<T, TResult>(
        [Self] this AkburaControl control,
        State<T> state,
        Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selector);

        var value = state.Value;
        return control.useComputed(() => selector(value), [state, value]);
    }

    [UseHook]
    public static State<T> useDistinct<T>(
        [Self] this AkburaControl control,
        State<T> state,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);

        comparer ??= EqualityComparer<T>.Default;
        var value = state.Value;
        var result = control.useHookState(value);
        control.useEffect((Action)(() =>
        {
            if (!comparer.Equals(result.Value, value))
            {
                result.Value = value;
            }
        }), [state, value, comparer]);

        return result;
    }

    private sealed class ComputedDependencies(object?[] values)
    {
        public object?[] Values = values;

        public bool Matches(ReadOnlySpan<object?> current)
        {
            if (Values.Length != current.Length)
            {
                return false;
            }

            for (var index = 0; index < current.Length; index++)
            {
                if (!EqualityComparer<object?>.Default.Equals(Values[index], current[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
