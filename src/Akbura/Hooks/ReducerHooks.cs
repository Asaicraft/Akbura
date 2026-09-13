using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

public static class ReducerHooks
{
    [UseHook]
    public static State<HookReducer<TState, TAction>> useReducer<TState, TAction>(
        [Self] this AkburaControl control,
        Func<TState, TAction, TState> reducer,
        TState initialValue)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        var value = control.useHookState(initialValue);
        return CreateReducer(control, reducer, value);
    }

    [UseHook]
    public static State<HookReducer<TState, TAction>> useReducer<TState, TAction>(
        [Self] this AkburaControl control,
        Func<TState, TAction, TState> reducer,
        Func<TState> initialize)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        ArgumentNullException.ThrowIfNull(initialize);
        var value = control.useHookState(initialize);
        return CreateReducer(control, reducer, value);
    }

    private static State<HookReducer<TState, TAction>> CreateReducer<TState, TAction>(
        AkburaControl control,
        Func<TState, TAction, TState> reducer,
        State<TState> value)
    {
        var result = control.useHookState(() => new HookReducer<TState, TAction>(control, value, reducer));
        control.useEffect((Action)(() => result.Value.SetReducer(reducer)));
        return result;
    }
}
