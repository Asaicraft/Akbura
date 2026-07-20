using Akbura.CompilerAnotations;

namespace Akbura.Hooks;

public static class EffectHooks
{
    [UseHook]
    public static void useEffect([Self] AkburaControl control, Action effect) =>
        Register(control, 0, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 1, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 2, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Action<CancellationToken> effect) =>
        Register(control, 3, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action<CancellationToken> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 4, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action<CancellationToken> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 5, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Action?> effect) =>
        Register(control, 6, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Action?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 7, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Action?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 8, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Action?> effect) =>
        Register(control, 9, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Action?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 10, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Action?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 11, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<IDisposable?> effect) =>
        Register(control, 12, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<IDisposable?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 13, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<IDisposable?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 14, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, IDisposable?> effect) =>
        Register(control, 15, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, IDisposable?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 16, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, IDisposable?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 17, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Task> effect) =>
        Register(control, 18, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 19, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 20, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Task> effect) =>
        Register(control, 21, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 22, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 23, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Task<Action?>> effect) =>
        Register(control, 24, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 25, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 26, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Task<Action?>> effect) =>
        Register(control, 27, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 28, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 29, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Task<IDisposable?>> effect) =>
        Register(control, 30, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 31, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 32, Normalize(effect), true, dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Task<IDisposable?>> effect) =>
        Register(control, 33, Normalize(effect), false, default, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, 34, Normalize(effect), true, dependencies, null);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, 35, Normalize(effect), true, dependencies, comparer);

    private static void Register(
        AkburaControl control,
        int overloadId,
        UseEffectCallback callback,
        bool hasDependencies,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer? comparer)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (hasDependencies && comparer == null && overloadId % 3 == 2)
        {
            throw new ArgumentNullException(nameof(comparer));
        }

        control.RegisterUseEffect(
            new UseHookKey(typeof(EffectHooks), overloadId),
            callback,
            hasDependencies,
            dependencies,
            comparer);
    }

    private static UseEffectCallback Normalize(Action effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _ =>
        {
            effect();
            return ValueTask.FromResult<IDisposable?>(null);
        };
    }

    private static UseEffectCallback Normalize(Action<CancellationToken> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return cancellationToken =>
        {
            effect(cancellationToken);
            return ValueTask.FromResult<IDisposable?>(null);
        };
    }

    private static UseEffectCallback Normalize(Func<Action?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _ => ValueTask.FromResult(ToDisposable(effect()));
    }

    private static UseEffectCallback Normalize(Func<CancellationToken, Action?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return cancellationToken => ValueTask.FromResult(ToDisposable(effect(cancellationToken)));
    }

    private static UseEffectCallback Normalize(Func<IDisposable?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _ => ValueTask.FromResult(effect());
    }

    private static UseEffectCallback Normalize(Func<CancellationToken, IDisposable?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return cancellationToken => ValueTask.FromResult(effect(cancellationToken));
    }

    private static UseEffectCallback Normalize(Func<Task> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async _ =>
        {
            await effect().ConfigureAwait(false);
            return null;
        };
    }

    private static UseEffectCallback Normalize(Func<CancellationToken, Task> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async cancellationToken =>
        {
            await effect(cancellationToken).ConfigureAwait(false);
            return null;
        };
    }

    private static UseEffectCallback Normalize(Func<Task<Action?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async _ => ToDisposable(await effect().ConfigureAwait(false));
    }

    private static UseEffectCallback Normalize(Func<CancellationToken, Task<Action?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async cancellationToken =>
            ToDisposable(await effect(cancellationToken).ConfigureAwait(false));
    }

    private static UseEffectCallback Normalize(Func<Task<IDisposable?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async _ => await effect().ConfigureAwait(false);
    }

    private static UseEffectCallback Normalize(Func<CancellationToken, Task<IDisposable?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async cancellationToken => await effect(cancellationToken).ConfigureAwait(false);
    }

    private static IDisposable? ToDisposable(Action? cleanup)
    {
        return cleanup == null ? null : new ActionUseEffectCleanup(cleanup);
    }
}
