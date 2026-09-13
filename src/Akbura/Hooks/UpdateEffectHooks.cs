using Akbura.CompilerAnotations;

namespace Akbura.Hooks;

public static class UpdateEffectHooks
{
    [UseHook]
    public static void useUpdateEffect(
        [Self] this AkburaControl control,
        Action effect,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var lifetime = PrepareLifetime(control);
        control.useEffect((Action)(() =>
        {
            if (ShouldRun(lifetime))
            {
                effect();
            }
        }), dependencies);
    }

    [UseHook]
    public static void useUpdateEffect(
        [Self] this AkburaControl control,
        Func<Action?> effect,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var lifetime = PrepareLifetime(control);
        control.useEffect((Func<Action?>)(() => ShouldRun(lifetime) ? effect() : null), dependencies);
    }

    [UseHook]
    public static void useUpdateEffect(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> effect,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var lifetime = PrepareLifetime(control);
        control.useEffect(
            (Func<CancellationToken, Task>)(token => ShouldRun(lifetime) ? effect(token) : Task.CompletedTask),
            dependencies);
    }

    private static HookRef<bool> PrepareLifetime(AkburaControl control)
    {
        var lifetime = control.useRef(false).Value;
        control.useEffect((Func<Action?>)(() =>
        {
            lifetime.Current = false;
            return () => lifetime.Current = false;
        }), []);
        return lifetime;
    }

    private static bool ShouldRun(HookRef<bool> lifetime)
    {
        if (lifetime.Current)
        {
            return true;
        }

        lifetime.Current = true;
        return false;
    }
}
