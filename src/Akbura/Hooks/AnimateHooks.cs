using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

public static class AnimateHooks
{
    private static readonly StateInfo<AnimationController> s_controller =
        new("animationController", static _ => new AnimationController());

    /// <summary>
    /// Returns a stable controller activated after a successful render and stopped
    /// when this hook lifetime ends. Disabled motion does not start new animations;
    /// cancellation of an existing run obeys that animation's fill mode.
    /// </summary>
    [UseHook]
    public static State<AnimationController> useAnimate(
        [Self] this AkburaControl control,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(control);
        var controller = control.useHookState(s_controller);
        control.useEffect(
            (Func<IDisposable?>)(() => controller.Value.Activate(enabled)),
            [enabled]);
        return controller;
    }
}
