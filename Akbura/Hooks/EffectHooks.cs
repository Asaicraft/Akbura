using Akbura.CompilerAnotations;

namespace Akbura.Hooks;

public static class EffectHooks
{
    [UseHook]
    public static void useEffect([Self] AkburaControl control, Action effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Action<CancellationToken> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action<CancellationToken> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Action<CancellationToken> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Action?> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Action?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Action?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Action?> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Action?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Action?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<IDisposable?> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<IDisposable?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<IDisposable?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, IDisposable?> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, IDisposable?> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, IDisposable?> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Task> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Task> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Task<Action?>> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Task<Action?>> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<Action?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<Task<IDisposable?>> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    [UseHook]
    public static void useEffect([Self] AkburaControl control, Func<CancellationToken, Task<IDisposable?>> effect) =>
        Register(control, effect, Normalize(effect));

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies) =>
        Register(control, effect, Normalize(effect), dependencies);

    [UseHook]
    public static void useEffect(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<IDisposable?>> effect,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer) =>
        Register(control, effect, Normalize(effect), dependencies, comparer);

    private static void Register<TCallback>(
        AkburaControl control,
        TCallback effect,
        UseEffectCallback callback)
        where TCallback : Delegate
    {
        ArgumentNullException.ThrowIfNull(effect);
        RegisterCore(
            control,
            UseEffectKeys<TCallback>.EveryRender,
            callback,
            false,
            default,
            null);
    }

    private static void Register<TCallback>(
        AkburaControl control,
        TCallback effect,
        UseEffectCallback callback,
        ReadOnlySpan<object?> dependencies)
        where TCallback : Delegate
    {
        ArgumentNullException.ThrowIfNull(effect);
        RegisterCore(
            control,
            UseEffectKeys<TCallback>.Dependencies,
            callback,
            true,
            dependencies,
            null);
    }

    private static void Register<TCallback>(
        AkburaControl control,
        TCallback effect,
        UseEffectCallback callback,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer comparer)
        where TCallback : Delegate
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(comparer);
        RegisterCore(
            control,
            UseEffectKeys<TCallback>.ComparedDependencies,
            callback,
            true,
            dependencies,
            comparer);
    }

    private static void RegisterCore(
        AkburaControl control,
        UseHookKey key,
        UseEffectCallback callback,
        bool hasDependencies,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer? comparer)
    {
        ArgumentNullException.ThrowIfNull(control);
        var registration = new UseEffectRegistration(
            key,
            callback,
            hasDependencies,
            dependencies,
            comparer);
        control.UseHook(
            registration.Key,
            registration,
            static current => new UseEffectSlot(current.Key),
            static (slot, current) => slot.Apply(current),
            static slot => slot.StopForDetach());
    }

    private static class UseEffectKeys<TCallback>
        where TCallback : Delegate
    {
        public static readonly UseHookKey EveryRender = new();

        public static readonly UseHookKey Dependencies = new();

        public static readonly UseHookKey ComparedDependencies = new();
    }

    internal static UseEffectCallback Normalize(Action effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _ =>
        {
            effect();
            return ValueTask.FromResult<IDisposable?>(null);
        };
    }

    internal static UseEffectCallback Normalize(Action<CancellationToken> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return cancellationToken =>
        {
            effect(cancellationToken);
            return ValueTask.FromResult<IDisposable?>(null);
        };
    }

    internal static UseEffectCallback Normalize(Func<Action?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _ => ValueTask.FromResult(ToDisposable(effect()));
    }

    internal static UseEffectCallback Normalize(Func<CancellationToken, Action?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return cancellationToken => ValueTask.FromResult(ToDisposable(effect(cancellationToken)));
    }

    internal static UseEffectCallback Normalize(Func<IDisposable?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return _ => ValueTask.FromResult(effect());
    }

    internal static UseEffectCallback Normalize(Func<CancellationToken, IDisposable?> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return cancellationToken => ValueTask.FromResult(effect(cancellationToken));
    }

    internal static UseEffectCallback Normalize(Func<Task> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async _ =>
        {
            await effect().ConfigureAwait(false);
            return null;
        };
    }

    internal static UseEffectCallback Normalize(Func<CancellationToken, Task> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async cancellationToken =>
        {
            await effect(cancellationToken).ConfigureAwait(false);
            return null;
        };
    }

    internal static UseEffectCallback Normalize(Func<Task<Action?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async _ => ToDisposable(await effect().ConfigureAwait(false));
    }

    internal static UseEffectCallback Normalize(Func<CancellationToken, Task<Action?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async cancellationToken =>
            ToDisposable(await effect(cancellationToken).ConfigureAwait(false));
    }

    internal static UseEffectCallback Normalize(Func<Task<IDisposable?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async _ => await effect().ConfigureAwait(false);
    }

    internal static UseEffectCallback Normalize(Func<CancellationToken, Task<IDisposable?>> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return async cancellationToken => await effect(cancellationToken).ConfigureAwait(false);
    }

    private static IDisposable? ToDisposable(Action? cleanup)
    {
        return cleanup == null ? null : new ActionUseEffectCleanup(cleanup);
    }
}
