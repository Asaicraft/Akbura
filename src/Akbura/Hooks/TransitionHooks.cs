using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;

namespace Akbura.Hooks;

public static class TransitionHooks
{
    private static readonly Easing s_defaultEasing = new CubicEaseOut();

    /// <summary>
    /// Installs an Avalonia transition after commit. Changes to the property's
    /// target value do not recreate the transition. Null targets are ignored.
    /// </summary>
    [UseHook]
    public static void useTransition<T>(
        [Self] this AkburaControl control,
        Animatable? target,
        AvaloniaProperty<T> property,
        int durationMilliseconds,
        Easing? easing = null,
        bool enabled = true) =>
        control.useTransition(target, property,
            TimeSpan.FromMilliseconds(durationMilliseconds), easing, enabled);

    [UseHook]
    public static void useTransition<T>(
        [Self] this AkburaControl control,
        Animatable? target,
        AvaloniaProperty<T> property,
        TimeSpan duration,
        Easing? easing = null,
        bool enabled = true)
    {
        ValidateTransition(control, property, duration);
        easing ??= s_defaultEasing;
        control.useTransitions(target,
            () => CreateTransitions(property, duration, easing),
            [property, duration, easing], enabled);
    }

    /// <summary>
    /// Resolves a named element after rendering, including the initial frame.
    /// Changing the resolver delegate alone does not recreate installed rules.
    /// </summary>
    [UseHook]
    public static void useTransition<T>(
        [Self] this AkburaControl control,
        Func<Animatable?> getTarget,
        AvaloniaProperty<T> property,
        int durationMilliseconds,
        Easing? easing = null,
        bool enabled = true) =>
        control.useTransition(getTarget, property,
            TimeSpan.FromMilliseconds(durationMilliseconds), easing, enabled);

    [UseHook]
    public static void useTransition<T>(
        [Self] this AkburaControl control,
        Func<Animatable?> getTarget,
        AvaloniaProperty<T> property,
        TimeSpan duration,
        Easing? easing = null,
        bool enabled = true)
    {
        ValidateTransition(control, property, duration);
        easing ??= s_defaultEasing;
        control.useTransitions(getTarget,
            () => CreateTransitions(property, duration, easing),
            [property, duration, easing], enabled);
    }

    /// <summary>
    /// Creates rules only when the resolved target, dependencies, enabled flag,
    /// or hook lifetime changes. The factory itself is not a dependency. Rules
    /// from styles and other hooks are preserved in a target-local copy.
    /// </summary>
    [UseHook]
    public static void useTransitions(
        [Self] this AkburaControl control,
        Animatable? target,
        Func<Transitions> createTransitions,
        ReadOnlySpan<object?> dependencies,
        bool enabled = true) =>
        control.useTransitions(() => target, createTransitions, dependencies, enabled);

    [UseHook]
    public static void useTransitions(
        [Self] this AkburaControl control,
        Func<Animatable?> getTarget,
        Func<Transitions> createTransitions,
        ReadOnlySpan<object?> dependencies,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(getTarget);
        ArgumentNullException.ThrowIfNull(createTransitions);

        var target = new AnimationHookTarget(getTarget);
        var effectDependencies = new object?[dependencies.Length + 2];
        dependencies.CopyTo(effectDependencies);
        effectDependencies[^2] = target;
        effectDependencies[^1] = enabled;

        control.useEffect((Func<Action?>)(() =>
        {
            var resolvedTarget = target.Resolve();
            if (!enabled || resolvedTarget is null)
            {
                return null;
            }

            var transitions = createTransitions() ?? throw new InvalidOperationException(
                "The transition factory returned null.");
            var rules = transitions.ToArray();
            TransitionCollectionOwner.Validate(resolvedTarget, rules);
            if (rules.Length == 0)
            {
                return null;
            }

            var lease = TransitionCollectionOwner.Add(resolvedTarget, rules);
            return lease.Dispose;
        }), effectDependencies, AnimationHookTarget.DependenciesComparer.Instance);
    }

    private static void ValidateTransition<T>(
        AkburaControl control,
        AvaloniaProperty<T> property,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(property);
        if (property.IsDirect)
        {
            throw new ArgumentException("Direct properties cannot be animated.", nameof(property));
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    private static Transitions CreateTransitions<T>(
        AvaloniaProperty<T> property,
        TimeSpan duration,
        Easing easing)
    {
        var type = typeof(T);
        TransitionBase transition = type == typeof(double) ? new DoubleTransition() :
            type == typeof(float) ? new FloatTransition() :
            type == typeof(int) ? new IntegerTransition() :
            type == typeof(bool) ? new BoolTransition() :
            type == typeof(Point) ? new PointTransition() :
            type == typeof(RelativePoint) ? new RelativePointTransition() :
            type == typeof(Size) ? new SizeTransition() :
            type == typeof(Vector) ? new VectorTransition() :
            type == typeof(Thickness) ? new ThicknessTransition() :
            type == typeof(CornerRadius) ? new CornerRadiusTransition() :
            type == typeof(Color) ? new ColorTransition() :
            type == typeof(IBrush) ? new BrushTransition() :
            type == typeof(BoxShadows) ? new BoxShadowsTransition() :
            type == typeof(ITransform) ? new TransformOperationsTransition() :
            throw new NotSupportedException($"No built-in transition is available for {type}.");

        transition.Property = property;
        transition.Duration = duration;
        transition.Easing = easing;
        return [transition];
    }
}
