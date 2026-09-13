using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

public static class HookStateHooks
{
    /// <summary>
    /// Gets this frame position's state, using the initial value only when the slot is created.
    /// </summary>
    [UseHook]
    public static State<T> useHookState<T>([Self] this AkburaControl control, T initialValue)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetHookState(StateDescriptors<T>.Value, initialValue);
    }

    /// <summary>
    /// Gets this frame position's state. The initializer must be pure and cannot call hooks.
    /// An aborted first frame can cause the initializer to run again on retry.
    /// </summary>
    [UseHook]
    public static State<T> useHookState<T>([Self] this AkburaControl control, Func<T> initialize)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(initialize);
        return control.GetHookState(StateDescriptors<T>.Value, initialize);
    }

    /// <summary>
    /// Gets this frame position's state using a stable descriptor shared across renders.
    /// Each position and component owns a separate state even when descriptors are shared.
    /// </summary>
    [UseHook]
    public static State<T> useHookState<T>([Self] this AkburaControl control, StateInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(info);
        return control.GetHookState(info);
    }

    /// <summary>
    /// Gets this frame position's state with a descriptor and an explicit initial value.
    /// </summary>
    [UseHook]
    public static State<T> useHookState<T>(
        [Self] this AkburaControl control,
        StateInfo<T> info,
        T initialValue)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(info);
        return control.GetHookState(info, initialValue);
    }

    private static class StateDescriptors<T>
    {
        public static readonly StateInfo<T> Value = new("hookState", static _ => default!);
    }
}
