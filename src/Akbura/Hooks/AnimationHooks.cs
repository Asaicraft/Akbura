using Akbura.CompilerAnotations;
using Avalonia.Animation;

namespace Akbura.Hooks;

public static class AnimationHooks
{
    /// <summary>
    /// Runs a finite keyframe animation after a committed frame. The factory is
    /// evaluated only when the target, explicit dependencies or enabled flag change.
    /// Disabling the hook leaves the target's base values in control.
    /// </summary>
    [UseHook]
    public static void useAnimation(
        [Self] this AkburaControl control,
        Animatable? target,
        Func<Animation> createAnimation,
        ReadOnlySpan<object?> dependencies,
        bool enabled = true) =>
        Register(control, new AnimationHookTarget(() => target), createAnimation, dependencies, enabled, null);

    /// <summary>
    /// Resolves a named or conditional target after UI generation. A null result
    /// means the target is absent and cancels any previous run.
    /// </summary>
    [UseHook]
    public static void useAnimation(
        [Self] this AkburaControl control,
        Func<Animatable?> target,
        Func<Animation> createAnimation,
        ReadOnlySpan<object?> dependencies,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        Register(control, new AnimationHookTarget(target), createAnimation, dependencies, enabled, null);
    }

    internal static void useAnimation(
        AkburaControl control,
        Animatable target,
        Func<Animation> createAnimation,
        ReadOnlySpan<object?> dependencies,
        bool enabled,
        Func<Animation, Animatable, CancellationToken, Task> runAnimation) =>
        Register(control, new AnimationHookTarget(() => target), createAnimation, dependencies, enabled, runAnimation);

    internal static void useAnimation(
        AkburaControl control,
        Func<Animatable?> target,
        Func<Animation> createAnimation,
        ReadOnlySpan<object?> dependencies,
        bool enabled,
        Func<Animation, Animatable, CancellationToken, Task> runAnimation) =>
        Register(control, new AnimationHookTarget(target), createAnimation, dependencies, enabled, runAnimation);

    private static void Register(
        AkburaControl control,
        AnimationHookTarget target,
        Func<Animation> createAnimation,
        ReadOnlySpan<object?> dependencies,
        bool enabled,
        Func<Animation, Animatable, CancellationToken, Task>? runAnimation)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(createAnimation);

        var effectDependencies = new object?[dependencies.Length + 2];
        dependencies.CopyTo(effectDependencies);
        effectDependencies[^2] = target;
        effectDependencies[^1] = enabled;

        control.useEffect((Func<CancellationToken, Task>)(async token =>
        {
            var resolvedTarget = target.Resolve();
            if (!enabled || resolvedTarget is null)
            {
                return;
            }

            token.ThrowIfCancellationRequested();
            var animation = createAnimation()
                ?? throw new InvalidOperationException("The animation factory returned null.");

            try
            {
                await AnimationPlayback.RunAsync(resolvedTarget, animation, token, runAnimation);
            }
            catch (AnimationTargetDetachedException)
            {
                // Removing a child is a normal lifetime boundary, not an effect failure.
            }
        }), effectDependencies, AnimationHookTarget.DependenciesComparer.Instance);
    }
}
