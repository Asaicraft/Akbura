using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

public static class StateHooks
{
    [UseHook]
    public static State<T> useState<T>([Self] this AkburaControl control, T initialValue) =>
        control.useHookState(initialValue);

    [UseHook]
    public static State<T> useState<T>([Self] this AkburaControl control, Func<T> initialize) =>
        control.useHookState(initialize);

    [UseHook]
    public static State<T> useState<T>([Self] this AkburaControl control, StateInfo<T> info) =>
        control.useHookState(info);

    [UseHook]
    public static State<T> useState<T>(
        [Self] this AkburaControl control,
        StateInfo<T> info,
        T initialValue) =>
        control.useHookState(info, initialValue);
}
