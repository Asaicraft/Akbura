using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia;

namespace Akbura.Hooks;

public static class AvaloniaPropertyHooks
{
    [UseHook]
    public static State<TValue> useAvaloniaProperty<TObject, TValue>(
        [Self] TObject control,
        AvaloniaProperty<TValue> property)
        where TObject : AvaloniaObject
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(property);

        var state = new State<TValue>((TValue)control.GetValue(property)!);
        var subscription = control
            .GetObservable(property)
            .Subscribe(new StateObserver<TValue>(state));
        state.RetainSubscription(subscription);
        return state;
    }

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Action effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Action<CancellationToken> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<Action?> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<CancellationToken, Action?> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<IDisposable?> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<CancellationToken, IDisposable?> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<Task> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<CancellationToken, Task> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<Task<Action?>> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<Action?>> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<Task<IDisposable?>> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    [UseHook]
    public static void useAvaloniaProperty(
        [Self] AkburaControl control,
        Func<CancellationToken, Task<IDisposable?>> effect,
        ReadOnlySpan<AvaloniaProperty> properties) =>
        Register(control, effect, EffectHooks.Normalize(effect), properties);

    private static void Register<TCallback>(
        AkburaControl control,
        TCallback effect,
        UseEffectCallback callback,
        ReadOnlySpan<AvaloniaProperty> properties)
        where TCallback : Delegate
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(effect);

        var arguments = new AvaloniaPropertyEffectArguments(
            AvaloniaPropertyEffectKeys<TCallback>.Key,
            callback,
            control,
            properties);
        control.UseHook(
            arguments.Key,
            arguments,
            static current => new AvaloniaPropertyEffectState(current.Key),
            static (state, current) => state.Apply(current),
            static state => state.StopForDetach());
    }

    private static class AvaloniaPropertyEffectKeys<TCallback>
        where TCallback : Delegate
    {
        public static readonly UseHookKey Key = new();
    }

    private sealed class StateObserver<T> : IObserver<T>
    {
        private readonly State<T> _state;

        public StateObserver(State<T> state)
        {
            _state = state;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnNext(T value)
        {
            _state.Value = value;
        }
    }
}
