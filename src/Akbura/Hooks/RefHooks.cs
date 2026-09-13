using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

public static class RefHooks
{
    [UseHook]
    public static State<HookRef<T>> useRef<T>([Self] this AkburaControl control, T initialValue) =>
        control.useHookState(() => new HookRef<T>(initialValue));

    [UseHook]
    public static State<HookRef<T>> useRef<T>([Self] this AkburaControl control, Func<T> initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        return control.useHookState(() => new HookRef<T>(initialize()));
    }

    /// <summary>
    /// Gets a cell updated after every successful frame. An aborted frame keeps its previous value.
    /// </summary>
    [UseHook]
    public static State<HookRef<T>> useLatest<T>([Self] this AkburaControl control, T value)
    {
        var result = control.useRef(value);
        control.useEffect((Action)(() => result.Value.Current = value));
        return result;
    }

    /// <summary>
    /// Gets a GUID-based ID stable until the hook slots are reset.
    /// </summary>
    [UseHook]
    public static State<string> useId([Self] this AkburaControl control, string prefix = "akbura")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return control.useHookState(() => $"{prefix}-{Guid.NewGuid():N}");
    }
}
