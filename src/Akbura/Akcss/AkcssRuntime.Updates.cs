using Avalonia;

namespace Akbura.Akcss;

internal static partial class AkcssRuntime
{
    internal static IDisposable BeginUpdate(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target is AvaloniaObject avaloniaObject)
        {
            avaloniaObject.VerifyAccess();
        }
        return s_targets.GetValue(target, static value => new TargetRuntime(value)).BeginUpdate();
    }

    internal static IDisposable? BeginExistingUpdate(object target) =>
        s_targets.TryGetValue(target, out var runtime) ? runtime.BeginUpdate() : null;

    private sealed partial class TargetRuntime
    {
        private int _updateDepth;
        private bool CannotFlush => _isApplying || _isChanging || _isDetached || _activeTransition != null || _updateDepth != 0;

        internal IDisposable BeginUpdate()
        {
            _updateDepth++;
            return new UpdateScope(this);
        }

        private sealed class UpdateScope(TargetRuntime runtime) : IDisposable
        {
            private TargetRuntime? _runtime = runtime;
            public void Dispose()
            {
                var owner = _runtime;
                if (owner == null)
                {
                    return;
                }
                if (owner._target is AvaloniaObject target)
                {
                    target.VerifyAccess();
                }
                _runtime = null;
                owner._updateDepth--;
                owner.ApplyPendingCandidates();
            }
        }
    }
}
