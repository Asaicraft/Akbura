using Avalonia.Animation;

namespace Akbura.Hooks;

/// <summary>
/// Captures a target after UI generation, not while render statements are collected.
/// The cached result belongs to this frame and cannot follow a later resolver closure.
/// </summary>
internal sealed class AnimationHookTarget(Func<Animatable?> resolve)
{
    private bool _resolved;
    private Animatable? _target;

    public Animatable? Resolve()
    {
        if (!_resolved)
        {
            _target = resolve();
            _resolved = true;
        }

        return _target;
    }

    internal sealed class DependenciesComparer : IUseHookDependenciesComparer
    {
        public static readonly DependenciesComparer Instance = new();

        public bool Equals(ReadOnlySpan<object?> previous, ReadOnlySpan<object?> current)
        {
            // Resolve even if other dependencies differ. The previous frame must
            // retain its target when a disabled effect is compared on a later frame.
            var previousTarget = ((AnimationHookTarget)previous[^2]!).Resolve();
            var currentTarget = ((AnimationHookTarget)current[^2]!).Resolve();
            if (!ReferenceEquals(previousTarget, currentTarget) || previous.Length != current.Length)
            {
                return false;
            }

            for (var index = 0; index < current.Length; index++)
            {
                if (index != current.Length - 2 &&
                    !EqualityComparer<object?>.Default.Equals(previous[index], current[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
