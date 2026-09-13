using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

/// <summary>
/// Tracks changes of one source across successfully completed renders.
/// </summary>
public static class PreviousHooks
{
    [UseHook]
    public static State<T> usePrevious<T>(
        [Self] this AkburaControl control,
        State<T> state,
        T initialValue = default!)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);

        var value = state.Value;
        var result = control.useHookState(initialValue);
        var history = control.useHookState(() => new SourceHistory<T>(state, value)).Value;
        control.useEffect((Action)(() =>
        {
            if (!ReferenceEquals(history.Source, state))
            {
                history.Source = state;
                history.Value = value;
                result.Value = result.InitialValue;
                return;
            }

            if (EqualityComparer<T>.Default.Equals(history.Value, value))
            {
                return;
            }

            var previous = history.Value;
            history.Value = value;
            result.Value = previous;
        }), [state, value]);

        return result;
    }

    [UseHook]
    public static void useOnChange<T>(
        [Self] this AkburaControl control,
        State<T> state,
        Action<T, T> callback)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(callback);

        var value = state.Value;
        var history = control.useHookState(() => new SourceHistory<T>(state, value)).Value;
        control.useEffect((Action)(() =>
        {
            if (!ReferenceEquals(history.Source, state))
            {
                history.Source = state;
                history.Value = value;
                return;
            }

            if (EqualityComparer<T>.Default.Equals(history.Value, value))
            {
                return;
            }

            var previous = history.Value;
            history.Value = value;
            callback(previous, value);
        }), [state, value]);
    }

    private sealed class SourceHistory<T>(State<T> source, T value)
    {
        public State<T> Source = source;
        public T Value = value;
    }
}
